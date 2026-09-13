using System.ComponentModel.DataAnnotations;

namespace ClaimPilot.API.Dtos;

public sealed record UploadDocumentRequest(
    [Required] string DocumentType,
    [Required] IFormFile File);
