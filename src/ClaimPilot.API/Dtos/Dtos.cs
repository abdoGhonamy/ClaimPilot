using System.ComponentModel.DataAnnotations;

using ClaimPilot.Domain.Enums;

namespace ClaimPilot.API.Dtos;

public sealed record LoginRequest([Required] string Username, [Required] string Password);

public sealed record LoginResponse(string Token, DateTime ExpiresAt, string Username, IReadOnlyList<string> Roles);

public sealed record ClaimDto(
    Guid Id,
    string ClaimNumber,
    string PolicyNumber,
    DateTime IncidentDate,
    decimal ClaimAmount,
    string Description,
    ClaimStatus Status,
    DateTime CreatedAt);

public sealed record RunDto(
    Guid RunId,
    Guid ClaimId,
    string ClaimNumber,
    string PolicyNumber,
    int? PolicyVersion,
    RunStatus Status,
    bool Degraded,
    bool ReviewRequired,
    Guid? ApprovalItemId,
    decimal? ProposedPayout,
    string? Summary,
    IReadOnlyList<string> Anomalies,
    DateTime CreatedAt);

public sealed record IngestDocumentRequest(
    [Required] string PolicyNumber,
    [Required] int Version,
    [Required] DateTime EffectiveDate,
    IFormFile File);

public sealed record IngestDocumentResponse(
    string DocumentReference,
    DocumentStatus Status,
    int ChunksCreated,
    int ChunksSkipped,
    string? Error,
    string? CorrelationId);

public sealed record AskRequest(
    [Required] string Question,
    [Required] string PolicyNumber,
    DateTime? IncidentDate);

public sealed record AskResponse(
    string Answer,
    bool Refused,
    string? RefusalReason,
    IReadOnlyList<CitationDto> Citations);

public sealed record CitationDto(
    string PolicyNumber,
    int Version,
    string? Section,
    string? Clause,
    int? Page,
    string TextExcerpt,
    string Source,
    float Score);

public sealed record ReviewApproveBody(string? Comment);
public sealed record ReviewRejectBody([Required] string Comment);
public sealed record ReviewEditBody([Required] string Comment, [Required] string EditedDecisionJson, decimal? EditedAmount);
public sealed record ReviewReReviewBody(string? Comment);
public sealed record ReviewAssignBody([Required] string AssigneeId, string? Comment);
public sealed record ReviewEscalateBody(string? Comment);
public sealed record ReviewPriorityBody([Required] Priority NewPriority, string? Comment);

public sealed record TraceViewResponse(
    string RunId,
    string ClaimNumber,
    string PolicyNumber,
    int? PolicyVersion,
    DateTime? EffectiveDate,
    string Status,
    bool Degraded,
    string? FailReason,
    IReadOnlyList<string> Agents,
    IReadOnlyList<string> ToolCalls,
    IReadOnlyList<string> RetrievedChunks,
    IReadOnlyList<string> ComputationSteps,
    IReadOnlyList<string> Anomalies,
    IReadOnlyList<UsageDto> Usage,
    decimal? EstimatedCost,
    IReadOnlyList<string> ApprovalHistory,
    string? FinalResult);

public sealed record UsageDto(
    string Provider,
    string Model,
    int InputTokens,
    int OutputTokens,
    decimal EstimatedCostUsd);

public sealed record ReviewQueueFilterRequest(ApprovalStatus? Status, string? AssigneeId, Priority? Priority);

public sealed record AuditDto(
    Guid Id,
    string EntityType,
    Guid? EntityId,
    string Action,
    string? ActorId,
    string? Before,
    string? After,
    string? CorrelationId,
    string? RunId,
    DateTime CreatedAt);