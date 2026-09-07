using ClaimPilot.Domain.Enums;

namespace ClaimPilot.Application.Interfaces.Documents;

/// <summary>Raw extracted text from a document, split into structural segments.</summary>
public sealed record ExtractedDocument(
    string FullText,
    IReadOnlyList<ExtractedSection> Sections);

public sealed record ExtractedSection(
    string Title,
    string Clause,
    int? Page,
    string Text);

/// <summary>Abstraction over file extraction (PDF / Markdown / DOCX).</summary>
public interface IFileTextExtractor
{
    bool Supports(string contentType, string fileName);
    Task<ExtractedDocument> ExtractAsync(Stream content, string fileName, CancellationToken ct);
}

/// <summary>Detection of sections and clauses → structural chunks.</summary>
public interface IChunkingStrategy
{
    IReadOnlyList<IngestedChunkPayload> Chunk(ExtractedDocument document);
}