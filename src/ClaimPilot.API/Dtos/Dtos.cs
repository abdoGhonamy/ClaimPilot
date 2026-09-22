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

public sealed record PolicyOptionDto(string PolicyNumber, string Name, string ProductLine);

public sealed record PolicyDetailDto(
    string PolicyNumber,
    string Name,
    string ProductLine,
    string Status,
    IReadOnlyList<PolicyWordingDto> Wordings);

public sealed record PolicyWordingDto(
    int Version,
    DateTime EffectiveDate,
    string Status,
    IReadOnlyList<CoverageItemDto> CoverageItems,
    IReadOnlyList<PolicyExclusionDto> Exclusions,
    IReadOnlyList<PolicySectionDto> Sections);

public sealed record CoverageItemDto(
    string Code, string Name, string Type, decimal? Amount, decimal? PercentageRate, string? Description);

public sealed record PolicyExclusionDto(string Code, string Name, string Description);

public sealed record PolicySectionDto(string Section, string Clause, int? Page, string Content);

public sealed record CoverageSeedItemDto(
    [Required, MaxLength(64)] string Code,
    [Required, MaxLength(256)] string Name,
    [Required] string Type,
    decimal? Amount,
    decimal? PercentageRate,
    string? Description);

public sealed record ExclusionSeedItemDto(
    [Required, MaxLength(64)] string Code,
    [Required, MaxLength(256)] string Name,
    [Required] string Description);

public sealed record SeedStructuredDataRequest(
    IReadOnlyList<CoverageSeedItemDto>? CoverageItems,
    IReadOnlyList<ExclusionSeedItemDto>? Exclusions);

public sealed record SeedStructuredDataResponse(
    int CoverageItemsInserted,
    int CoverageItemsSkipped,
    int ExclusionsInserted,
    int ExclusionsSkipped);

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
public sealed record ReviewAssignBody([Required] AssigneeRole Assignee, string? Comment);
public sealed record ReviewEscalateBody(string? Comment);
public sealed record ReviewPriorityBody([Required] Priority NewPriority, string? Comment);

public sealed record ReviewExplanationDto(
    Guid RunId,
    IReadOnlyList<ReviewExclusionDto> Exclusions,
    IReadOnlyList<ReviewAnomalyDto> Anomalies,
    IReadOnlyList<ReviewComputationStepDto> ComputationSteps,
    string? InsufficiencyReason);

public sealed record ReviewExclusionDto(string Code, string? Name, bool Applies, string? Evidence);
public sealed record ReviewAnomalyDto(string Type, string Severity, string Description, string? Evidence);
public sealed record ReviewComputationStepDto(string Step, string Description, decimal? Amount, string? Detail);

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

public sealed record ReviewQueueFilterRequest(ApprovalStatus? Status, AssigneeRole? AssigneeId, Priority? Priority);

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
