using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using ClaimPilot.Application.Interfaces.Review;
using ClaimPilot.Domain.Enums;
using ClaimPilot.Infrastructure.Data;

namespace ClaimPilot.Infrastructure.Services;

/// <summary>
/// Background worker that reassigns stalled approval items:
/// rule 1 — an Adjuster-assigned item pending more than 8h moves to Supervisor;
/// rule 2 — a Supervisor-assigned item pending more than 16h moves to Director;
/// rule 3 — an unassigned item older than 2h is routed by priority.
/// Reassignment updates the assignee and resets AssignedAt via IApprovalService.
/// </summary>
public sealed class SlaEscalationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SlaEscalationWorker> _logger;
    private readonly IRedisCache _redis;

    public SlaEscalationWorker(
        IServiceScopeFactory scopeFactory,
        IRedisCache redis,
        ILogger<SlaEscalationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _redis = redis;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SLA escalation sweep failed.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        // Simple coordination lock via Redis to avoid duplicate reassignments in multi-instance runs.
        var lockKey = "claimpilot:sla:sweep";
        var acquired = await _redis.SetIfNotExistsAsync(lockKey, TimeSpan.FromMinutes(1));
        if (!acquired) return;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var approvalService = scope.ServiceProvider.GetRequiredService<IApprovalService>();

            var pending = db.ApprovalItems.Where(i => i.Status == ApprovalStatus.Pending).ToList();
            var now = DateTime.UtcNow;

            foreach (var item in pending)
            {
                var assignedSince = item.AssignedAt ?? item.CreatedAt;
                var elapsed = now - assignedSince;
                AssigneeRole? nextAssignee = null;

                if (item.AssignedTo == AssigneeRole.Adjuster && elapsed > TimeSpan.FromHours(8))
                {
                    nextAssignee = AssigneeRole.Supervisor;
                }
                else if (item.AssignedTo == AssigneeRole.Supervisor && elapsed > TimeSpan.FromHours(16))
                {
                    nextAssignee = AssigneeRole.Director;
                }
                else if (item.AssignedTo is null && elapsed > TimeSpan.FromHours(2))
                {
                    nextAssignee = item.Priority switch
                    {
                        Priority.Critical => AssigneeRole.Director,
                        Priority.High => AssigneeRole.Supervisor,
                        _ => AssigneeRole.Adjuster
                    };
                }

                if (nextAssignee.HasValue)
                {
                    await approvalService.AssignAsync(item.Id,
                        new AssignRequest(nextAssignee.Value, "sla-worker",
                            "SLA rule triggered; item reassigned."), ct);
                }
            }
        }
        finally
        {
            await _redis.DeleteAsync(lockKey);
        }
    }
}