using System.Text.Json;
using System.Threading.Channels;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using ClaimPilot.API.Dtos;
using ClaimPilot.API.Helpers;
using ClaimPilot.Application.Interfaces;
using ClaimPilot.Application.Interfaces.Documents;
using ClaimPilot.Application.Interfaces.Orchestration;
using ClaimPilot.Application.Interfaces.Repositories;
using ClaimPilot.Application.Interfaces.Trace;
using ClaimPilot.Domain.Entities;
using ClaimPilot.Domain.Enums;
using ClaimPilot.Domain.Exceptions;

namespace ClaimPilot.API.Controllers;

[ApiController]
[Route("api/claims")]
[Authorize(Roles = "Adjuster,Supervisor,Viewer")]
public sealed class ClaimsController : ControllerBase
{
    private readonly IClaimRepository _claims;
    private readonly IPolicyRepository _policies;
    private readonly IAuditService _audit;
    private readonly IStorageService _storage;
    private readonly IRunTraceViewBuilder _traceView;

    public ClaimsController(
        IClaimRepository claims,
        IPolicyRepository policies,
        IAuditService audit,
        IStorageService storage,
        IRunTraceViewBuilder traceView)
    {
        _claims = claims;
        _policies = policies;
        _audit = audit;
        _storage = storage;
        _traceView = traceView;
    }

    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ClaimDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ClaimDto>>> List(CancellationToken ct)
    {
        var all = await _claims.GetAllAsync(ct);
        return Ok(all.Select(c => new ClaimDto(c.Id, c.ClaimNumber, c.PolicyNumber, c.IncidentDate,
            c.ClaimAmount, c.Description, c.Status, c.CreatedAt)).ToList());
    }

    [HttpGet("{claimId:guid}")]
    [ProducesResponseType<ClaimDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ClaimDto>> Get(Guid claimId, CancellationToken ct)
    {
        var claim = await _claims.GetByIdAsync(claimId, ct)
            ?? throw new DomainException($"Claim {claimId} not found.");
        return Ok(new ClaimDto(claim.Id, claim.ClaimNumber, claim.PolicyNumber, claim.IncidentDate,
            claim.ClaimAmount, claim.Description, claim.Status, claim.CreatedAt));
    }

    /// <summary>
    /// Creates a new claim for an existing policy. Assigns a per-year sequential
    /// ClaimNumber inside the repository and records a "Created" audit entry.
    /// </summary>
    /// <param name="request">Claim details (policy number, incident date, amount, description).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>201 with the created claim and a Location header.</returns>
    [HttpPost]
    [Authorize(Roles = "Adjuster,Supervisor")]
    [ProducesResponseType<ClaimDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ClaimDto>> Create([FromBody] CreateClaimRequest request, CancellationToken ct)
    {
        ValidateNewClaim(request);

        var policy = await _policies.GetByPolicyNumberAsync(request.PolicyNumber, ct)
            ?? throw new DomainException($"Policy '{request.PolicyNumber}' not found.");

        var claim = new Claim
        {
            Id = Guid.NewGuid(),
            ClaimNumber = await _claims.GenerateClaimNumberAsync(ct),
            PolicyNumber = policy.PolicyNumber,
            IncidentDate = EnsureUtc(request.IncidentDate),
            ClaimAmount = request.ClaimAmount,
            Description = request.Description.Trim(),
            Status = ClaimStatus.Submitted,
            CreatedAt = DateTime.UtcNow
        };

        await _claims.AddAsync(claim, ct);

        await _audit.RecordAsync(new AuditLogEntry(
            "Claim", claim.Id, "Created",
            User.Identity?.Name ?? "system",
            After: JsonSerializer.Serialize(new { claim.ClaimNumber, claim.PolicyNumber, claim.ClaimAmount })), ct);

        var dto = new ClaimDto(claim.Id, claim.ClaimNumber, claim.PolicyNumber, claim.IncidentDate,
            claim.ClaimAmount, claim.Description, claim.Status, claim.CreatedAt);

        return CreatedAtAction(nameof(Get), new { claimId = claim.Id }, dto);
    }

    /// <summary>
    /// Attaches a supporting document to an existing claim. The file is streamed to the
    /// storage root (never wwwroot) under a generated, non-trusting name and registered
    /// as a ClaimDocument row with an "Uploaded" audit entry.
    /// </summary>
    /// <param name="claimId">The claim receiving the document.</param>
    /// <param name="request">Document type and the multipart file.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>201 with the stored document metadata and a Location header.</returns>
    [HttpPost("{claimId:guid}/documents")]
    [Authorize(Roles = "Adjuster,Supervisor")]
    [RequestSizeLimit(20_000_000)]
    [ProducesResponseType<ClaimDocumentDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ClaimDocumentDto>> UploadDocument(
        Guid claimId, [FromForm] UploadDocumentRequest request, CancellationToken ct)
    {
        var claim = await _claims.GetByIdAsync(claimId, ct);
        if (claim is null) return NotFound();

        var file = request.File;
        var resolvedType = ClaimUploadRules.ValidateAndResolve(request.DocumentType, file?.FileName ?? string.Empty, file?.Length ?? 0);
        var extension = Path.GetExtension(file!.FileName).ToLowerInvariant();

        await using var stream = file.OpenReadStream();
        if (!ContentTypeSniffer.Matches(stream, resolvedType))
            throw new ValidationException("File content does not match its declared type.");
        stream.Position = 0;

        var documentId = Guid.NewGuid();
        var stored = await _storage.SaveAsync(claimId, documentId, extension, stream, ct);

        // documentType is validated but not persisted: the ClaimDocument schema carries no
        // type column, and the task forbids schema changes.
        var document = new ClaimDocument
        {
            Id = documentId,
            ClaimId = claim.Id,
            Claim = claim,
            FileName = Path.GetFileName(file.FileName),
            ContentType = resolvedType,
            SizeBytes = stored.SizeBytes,
            StoredAt = DateTime.UtcNow
        };

        var saved = await _claims.AddDocumentAsync(document, ct);

        await _audit.RecordAsync(new AuditLogEntry(
            "ClaimDocument", saved.Id, "Uploaded",
            User.Identity?.Name ?? "system",
            After: JsonSerializer.Serialize(new { saved.FileName, saved.SizeBytes, saved.ContentType })), ct);

        var dto = new ClaimDocumentDto(saved.Id, saved.ClaimId, saved.FileName, saved.ContentType,
            saved.SizeBytes, saved.StoredAt);

        return Created($"/api/claims/{claimId}/documents/{saved.Id}", dto);
    }

    [HttpGet("{claimId:guid}/runs")]
    public async Task<ActionResult<IReadOnlyList<RunDto>>> Runs(Guid claimId, CancellationToken ct)
    {
        var claim = await _claims.GetByIdAsync(claimId, ct);
        if (claim is null) return NotFound();

        return Ok(claim.Runs.OrderByDescending(r => r.CreatedAt).Select(r => new RunDto(
            r.Id, r.ClaimId, claim.ClaimNumber, claim.PolicyNumber, null,
            r.Status, r.Degraded, r.ApprovalItems.Any(),
            r.ApprovalItems.LastOrDefault()?.Id, r.FinalDecision?.ApprovedAmount,
            r.FinalDecision?.Rationale, r.Anomalies.Select(a => a.Description).ToList(), r.CreatedAt)).ToList());
    }

    [HttpGet("runs/{runId:guid}/trace")]
    [ProducesResponseType<TraceViewResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<TraceViewResponse>> Trace(Guid runId, CancellationToken ct)
    {
        var view = await _traceView.BuildAsync(runId.ToString(), ct);
        if (view is null) return NotFound();

        return Ok(new TraceViewResponse(view.RunId, view.ClaimNumber, view.PolicyNumber, view.PolicyVersion,
            view.EffectiveDate, view.Status, view.Degraded, view.FailReason, view.Agents, view.ToolCalls,
            view.RetrievedChunks, view.ComputationSteps, view.Anomalies,
            view.Usage.Select(u => new UsageDto(u.Provider, u.Model, u.InputTokens, u.OutputTokens, u.EstimatedCostUsd)).ToList(),
            view.EstimatedCost, view.ApprovalHistory, view.FinalResult));
    }

    /// <summary>
    /// Runs the supervisor workflow and streams a live SSE trace of
    /// orchestrator events (agent activity, tool calls, policy version selection)
    /// followed by the final run result. Requires Adjuster or Supervisor.
    /// </summary>
    [HttpPost("{claimId:guid}/adjudicate")]
    [Authorize(Roles = "Adjuster,Supervisor")]
    public async Task Adjudicate(Guid claimId, [FromQuery] string? correlationId, CancellationToken ct)
    {
        Response.Headers.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";

        var orchestrator = HttpContext.RequestServices.GetRequiredService<IClaimsOrchestrator>();
        var channel = Channel.CreateUnbounded<string>();

        OrchestrationEventHandler handler = (e, token) =>
        {
            channel.Writer.TryWrite(SerializeEvent("event", e.EventType, e.RunId, e.Agent, e.Tool, e.Payload));
            return Task.CompletedTask;
        };
        orchestrator.EventRaised += handler;

        try
        {
            var runTask = Task.Run(() => orchestrator.RunAsync(claimId, correlationId, ct), ct);
            var drainTask = Task.Run(async () =>
            {
                await foreach (var message in channel.Reader.ReadAllAsync(ct))
                {
                    await Response.WriteAsync(message, ct);
                    await Response.Body.FlushAsync(ct);
                }
            }, ct);

            var run = await runTask;
            channel.Writer.TryComplete();
            await drainTask;

            await Response.WriteAsync(SerializeEvent("run_complete", run.Status, run.RunId, null, null,
                System.Text.Json.JsonSerializer.Serialize(run)), ct);
        }
        catch (Exception ex)
        {
            channel.Writer.TryComplete();
            await Response.WriteAsync(SerializeEvent("error", "error", claimId, null, null,
                System.Text.Json.JsonSerializer.Serialize(new { message = ex.Message })), CancellationToken.None);
        }
        finally
        {
            orchestrator.EventRaised -= handler;
            channel.Writer.TryComplete();
        }
    }

    private static string SerializeEvent(
        string eventName, string? eventType, Guid runId, string? agent, string? tool, string? payload) =>
        $"event: {eventName}\ndata: {System.Text.Json.JsonSerializer.Serialize(new
        {
            event_type = eventType,
            run_id = runId,
            agent,
            tool,
            payload
        })}\n\n";

    private static DateTime EnsureUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc
            ? value
            : value.Kind == DateTimeKind.Local
                ? value.ToUniversalTime()
                : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static void ValidateNewClaim(CreateClaimRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.PolicyNumber))
            throw new ValidationException("PolicyNumber is required.");
        if (request.PolicyNumber.Length > 64)
            throw new ValidationException("PolicyNumber must not exceed 64 characters.");
        if (request.IncidentDate == default)
            throw new ValidationException("IncidentDate is required.");
        if (EnsureUtc(request.IncidentDate) > DateTime.UtcNow.AddMinutes(1))
            throw new ValidationException("IncidentDate must not be in the future.");
        if (request.ClaimAmount <= 0)
            throw new ValidationException("ClaimAmount must be greater than zero.");
        if (request.ClaimAmount > 10_000_000)
            throw new ValidationException("ClaimAmount must not exceed 10,000,000.");
        if (string.IsNullOrWhiteSpace(request.Description))
            throw new ValidationException("Description is required.");
        if (request.Description.Length < 20)
            throw new ValidationException("Description must be at least 20 characters.");
        if (request.Description.Length > 4000)
            throw new ValidationException("Description must not exceed 4000 characters.");
    }
}