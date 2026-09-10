using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using ClaimPilot.API.Dtos;
using ClaimPilot.Application.Interfaces.Documents;

namespace ClaimPilot.API.Controllers;

[ApiController]
[Route("api/documents")]
[Authorize(Roles = "Adjuster,Supervisor")]
public sealed class DocumentsController : ControllerBase
{
    private readonly IDocumentIngestionService _ingestion;

    public DocumentsController(IDocumentIngestionService ingestion) => _ingestion = ingestion;

    /// <summary>
    /// Ingests a policy wording file (pdf/md/docx), extracts text, chunks it
    /// structurally, embeds it with Ollama and stores chunk citations.
    /// Idempotent: re-uploading the same content skips existing chunks.
    /// </summary>
    [HttpPost("ingest")]
    [RequestSizeLimit(20_000_000)]
    [ProducesResponseType<IngestDocumentResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IngestDocumentResponse>> Ingest(
        [FromForm] string policyNumber,
        [FromForm] int version,
        [FromForm] DateTime effectiveDate,
        IFormFile file,
        CancellationToken ct)
    {
        await using var stream = file.OpenReadStream();
        var result = await _ingestion.IngestAsync(
            policyNumber, version, effectiveDate, file.FileName, file.ContentType, stream, ct);

        return Ok(new IngestDocumentResponse(result.DocumentReference, result.Status,
            result.ChunksCreated, result.ChunksSkipped, result.Error, result.CorrelationId));
    }
}