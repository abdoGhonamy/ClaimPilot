using System.Xml.Linq;

using UglyToad.PdfPig;

using ClaimPilot.Application.Interfaces.Documents;

namespace ClaimPilot.Infrastructure.Services.Documents;

/// <summary>PDF text extractor using PdfPig (pure managed .NET).</summary>
public sealed class PdfTextExtractor : IFileTextExtractor
{
    public bool Supports(string contentType, string fileName)
        => contentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase) ||
           fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);

    public Task<ExtractedDocument> ExtractAsync(Stream content, string fileName, CancellationToken ct)
    {
        var sections = new List<ExtractedSection>();
        using var pdf = PdfDocument.Open(content);

        foreach (var page in pdf.GetPages())
        {
            ct.ThrowIfCancellationRequested();
            var text = page.Text;
            var section = new ExtractedSection("page", string.Empty, (int)page.Number, text);
            sections.Add(section);
        }

        var fullText = string.Join('\n', sections.Select(s => s.Text));
        return Task.FromResult(new ExtractedDocument(fullText, sections));
    }
}

/// <summary>Markdown text extractor.</summary>
public sealed class MarkdownTextExtractor : IFileTextExtractor
{
    public bool Supports(string contentType, string fileName)
        => contentType.Contains("markdown", StringComparison.OrdinalIgnoreCase) ||
           contentType.Equals("text/markdown", StringComparison.OrdinalIgnoreCase) ||
           fileName.EndsWith(".md", StringComparison.OrdinalIgnoreCase);

    public Task<ExtractedDocument> ExtractAsync(Stream content, string fileName, CancellationToken ct)
    {
        using var reader = new StreamReader(content);
        var text = reader.ReadToEnd();
        var sections = new List<ExtractedSection>();
        foreach (var line in text.Split('\n'))
        {
            if (line.StartsWith('#') && line.Trim().Length > 2)
            {
                var title = line.Trim().TrimStart('#').Trim();
                sections.Add(new ExtractedSection(title, string.Empty, null, string.Empty));
            }
            else if (sections.Count > 0)
            {
                var last = sections[^1];
                sections[^1] = new ExtractedSection(last.Title, last.Clause, last.Page, last.Text + line + "\n");
            }
            else
            {
                sections.Add(new ExtractedSection("README", string.Empty, null, line + "\n"));
            }
        }
        return Task.FromResult(new ExtractedDocument(text, sections));
    }
}

/// <summary>DOCX text extractor (reads word/document.xml from the Open XML zip).</summary>
public sealed class DocxTextExtractor : IFileTextExtractor
{
    public bool Supports(string contentType, string fileName)
        => contentType.Equals("application/vnd.openxmlformats-officedocument.wordprocessingml.document",
               StringComparison.OrdinalIgnoreCase) ||
           fileName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase);

    public Task<ExtractedDocument> ExtractAsync(Stream content, string fileName, CancellationToken ct)
    {
        using var archive = new System.IO.Compression.ZipArchive(content, System.IO.Compression.ZipArchiveMode.Read);
        var entry = archive.GetEntry("word/document.xml")
            ?? throw new InvalidOperationException("Invalid DOCX: missing word/document.xml.");

        using var stream = entry.Open();
        var doc = XDocument.Load(stream);
        var paragraphs = doc.Descendants(XName.Get("p", "http://schemas.openxmlformats.org/wordprocessingml/2006/main"))
            .Select(p => string.Concat(
                p.Descendants(XName.Get("t", "http://schemas.openxmlformats.org/wordprocessingml/2006/main"))
                    .Select(t => t.Value)))
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();

        var fullText = string.Join('\n', paragraphs);
        var sections = paragraphs.Select(p =>
            new ExtractedSection(Truncate(p, 40), string.Empty, null, p)).ToList();

        return Task.FromResult(new ExtractedDocument(fullText, sections));
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max];
}