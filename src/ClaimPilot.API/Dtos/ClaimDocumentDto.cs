namespace ClaimPilot.API.Dtos;

public sealed record ClaimDocumentDto(
    Guid Id,
    Guid ClaimId,
    string FileName,
    string ContentType,
    long SizeBytes,
    DateTime StoredAt);
