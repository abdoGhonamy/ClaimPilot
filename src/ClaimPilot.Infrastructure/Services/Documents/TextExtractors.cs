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
            var text = ReconstructPageLines(page);
            var section = new ExtractedSection("page", string.Empty, (int)page.Number, text);
            sections.Add(section);
        }

        var fullText = string.Join('\n', sections.Select(s => s.Text));
        return Task.FromResult(new ExtractedDocument(fullText, sections));
    }

    private static string ReconstructPageLines(UglyToad.PdfPig.Content.Page page)
    {
        var words = page.GetWords()
            .Where(w => !string.IsNullOrWhiteSpace(w.Text))
            .OrderByDescending(w => (double)w.BoundingBox.Bottom)
            .ThenBy(w => (double)w.BoundingBox.Left)
            .ToList();

        var lines = new List<string>();
        var current = new List<string>();
        double? lineBottom = null;
        foreach (var w in words)
        {
            var bottom = (double)w.BoundingBox.Bottom;
            var height = (double)w.BoundingBox.Height;
            if (lineBottom.HasValue && Math.Abs(bottom - lineBottom.Value) > Math.Max(2.0, height * 0.5))
            {
                lines.Add(string.Join(" ", current));
                current = new List<string>();
            }
            lineBottom = bottom;
            current.Add(w.Text);
        }

        if (current.Count > 0)
            lines.Add(string.Join(" ", current));

        return string.Join("\n", lines);
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
        var text = reader.ReadToEnd().Replace("\r\n", "\n");
        var sections = new List<ExtractedSection>
        {
            new ExtractedSection("markdown", string.Empty, null, text)
        };
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
        var sections = new List<ExtractedSection>
        {
            new ExtractedSection("docx", string.Empty, null, fullText)
        };

        return Task.FromResult(new ExtractedDocument(fullText, sections));
    }
}