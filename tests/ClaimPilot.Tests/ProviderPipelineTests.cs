using System.Net;
using System.Text;

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using ClaimPilot.Application.Interfaces.AI;
using ClaimPilot.Infrastructure.Services;

namespace ClaimPilot.Tests;

public sealed class ProviderPipelineTests
{
    [Fact]
    public async Task GeminiPrimary_UsesGemini_WhenItsRequestSucceeds()
    {
        var gemini = Gemini(new StubHandler(_ => Json(HttpStatusCode.OK, GeminiResponse("Gemini answer"))), "key");
        var ollama = Ollama(new StubHandler(_ => Json(HttpStatusCode.OK, OllamaResponse("Ollama answer"))));
        var context = new FakeAiPipelineContext();
        var provider = new FallbackLLMProvider(ollama, gemini, context, NullLogger<FallbackLLMProvider>.Instance);

        var result = await provider.CompleteAsync("system", "question", null, CancellationToken.None);

        result.Provider.Should().Be("gemini");
        result.Text.Should().Be("Gemini answer");
        context.Current.Should().Be(AiPipeline.Gemini);
    }

    [Fact]
    public async Task GeminiFailure_SwitchesEntireGenerationPipeline_ToOllama()
    {
        var gemini = Gemini(new StubHandler(_ => Json(HttpStatusCode.ServiceUnavailable, "{}")), "key");
        var ollama = Ollama(new StubHandler(_ => Json(HttpStatusCode.OK, OllamaResponse("Local answer"))));
        var context = new FakeAiPipelineContext();
        var provider = new FallbackLLMProvider(ollama, gemini, context, NullLogger<FallbackLLMProvider>.Instance);

        var result = await provider.CompleteAsync("system", "question", null, CancellationToken.None);

        result.Provider.Should().Be("ollama");
        result.Text.Should().Be("Local answer");
        context.Current.Should().Be(AiPipeline.Ollama);
        context.FallbackReason.Should().Be("HttpRequestException");
    }

    [Fact]
    public async Task GeminiEmbedding_ParsesOnlyItsOwnVectorSpace()
    {
        var provider = new GeminiEmbeddingProvider(
            new HttpClient(new StubHandler(_ => Json(HttpStatusCode.OK, GeminiEmbeddingResponse()))),
            Options.Create(new GeminiOptions { ApiKey = "key", EmbeddingModel = "gemini-embedding-2" }));

        var result = await provider.EmbedAsync("collision deductible", CancellationToken.None);

        result.Provider.Should().Be("gemini");
        result.Model.Should().Be("gemini-embedding-2");
        result.Vector.Should().HaveCount(768);
        result.Vector[0].Should().Be(0.25f);
    }

    [Fact]
    public void EmbeddingResolver_MapsEachPipeline_ToTheMatchingProvider()
    {
        var gemini = new GeminiEmbeddingProvider(new HttpClient(new StubHandler(_ => Json(HttpStatusCode.OK, GeminiEmbeddingResponse()))),
            Options.Create(new GeminiOptions { ApiKey = "key" }));
        var ollama = new OllamaEmbeddingProvider(new HttpClient(new StubHandler(_ => Json(HttpStatusCode.OK, OllamaEmbeddingResponse()))),
            Options.Create(new OllamaOptions()), NullLogger<OllamaEmbeddingProvider>.Instance);
        var resolver = new EmbeddingProviderResolver(gemini, ollama);

        resolver.Get(AiPipeline.Gemini).ProviderName.Should().Be("gemini");
        resolver.Get(AiPipeline.Ollama).ProviderName.Should().Be("ollama");
    }

    private static GeminiLLMProvider Gemini(HttpMessageHandler handler, string key) => new(
        new HttpClient(handler), Options.Create(new GeminiOptions { ApiKey = key }));

    private static OllamaLLMProvider Ollama(HttpMessageHandler handler) => new(
        new HttpClient(handler), Options.Create(new OllamaOptions()), NullLogger<OllamaLLMProvider>.Instance);

    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private static string GeminiResponse(string text) => "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"" + text + "\"}]}}],\"usageMetadata\":{\"promptTokenCount\":4,\"candidatesTokenCount\":2}}";
    private static string OllamaResponse(string text) => "{\"message\":{\"content\":\"" + text + "\"},\"prompt_eval_count\":4,\"eval_count\":2}";
    private static string OllamaEmbeddingResponse() => "{\"embedding\":[0.5,0.1]}";
    private static string GeminiEmbeddingResponse() => "{\"embedding\":{\"values\":[" + string.Join(',', Enumerable.Repeat("0.25", 768)) + "]}}";

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(respond(request));
    }
}
