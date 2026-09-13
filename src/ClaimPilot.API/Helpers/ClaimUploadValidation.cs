using ClaimPilot.Domain.Exceptions;

namespace ClaimPilot.API.Helpers;

/// <summary>Allowed document types and content types for claim document uploads.</summary>
public static class ClaimUploadRules
{
    public static readonly IReadOnlySet<string> AllowedDocumentTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "PoliceReport", "Photo", "Receipt", "MedicalReport", "Invoice", "Other"
    };

    public static readonly IReadOnlySet<string> AllowedContentTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf", "image/jpeg", "image/png", "image/webp", "image/heic"
    };

    private static readonly IReadOnlyDictionary<string, string> ExtensionByContentType = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["application/pdf"] = ".pdf",
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/webp"] = ".webp",
        ["image/heic"] = ".heic"
    };

    public static readonly IReadOnlySet<string> AllowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".jpg", ".jpeg", ".png", ".webp", ".heic"
    };

    public static string ContentTypeForExtension(string extension)
    {
        if (string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase))
            return "image/jpeg";
        return ExtensionByContentType.FirstOrDefault(kv =>
                string.Equals(kv.Value, extension, StringComparison.OrdinalIgnoreCase)).Key
            ?? string.Empty;
    }

    public static string ValidateAndResolve(string documentType, string fileName, long length)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ValidationException("A file is required.");
        if (length <= 0)
            throw new ValidationException("The uploaded file is empty.");
        if (length > 20_000_000)
            throw new ValidationException("The uploaded file must not exceed 20 MB.");
        if (!AllowedDocumentTypes.Contains(documentType))
            throw new ValidationException($"'{documentType}' is not a supported claim document type.");

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension))
            throw new ValidationException($"'{extension}' is not a supported file type.");

        var contentType = ContentTypeForExtension(extension);
        if (!AllowedContentTypes.Contains(contentType))
            throw new ValidationException($"File type '{contentType}' is not allowed.");

        return contentType;
    }
}

/// <summary>Minimal magic-byte validation so a file's declared type matches its content.</summary>
public static class ContentTypeSniffer
{
    public static bool Matches(Stream stream, string contentType)
    {
        var header = new byte[16];
        var read = stream.Read(header, 0, header.Length);
        stream.Position = 0;

        return contentType switch
        {
            "application/pdf" => read >= 4 && header[0] == 0x25 && header[1] == 0x50 && header[2] == 0x44 && header[3] == 0x46,
            "image/jpeg" => read >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF,
            "image/png" => read >= 8 && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47,
            "image/webp" => read >= 12 && header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46 &&
                            header[8] == 0x57 && header[9] == 0x45 && header[10] == 0x42 && header[11] == 0x50,
            "image/heic" => read >= 12 && header[4] == 0x66 && header[5] == 0x74 && header[6] == 0x79 && header[7] == 0x70,
            _ => false
        };
    }
}
