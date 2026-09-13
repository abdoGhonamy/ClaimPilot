using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using ClaimPilot.Application.Interfaces;
using ClaimPilot.Application.Interfaces.Assignment;
using ClaimPilot.Application.Interfaces.Repositories;
using ClaimPilot.Application.Interfaces.Review;
using ClaimPilot.Domain.Enums;

namespace ClaimPilot.Infrastructure.Services;

/// <summary>
/// Background worker that enforces SLA escalation rules. Runs on a timer,
/// checks pending unassigned/unreviewed/late items and escalates according to
/// the configured SlaRuleSet. Rules are configuration, never hardcoded here.
/// </summary>
public sealed class SlaEscalationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<SlaRuleSet> _rules;
    private readonly ILogger<SlaEscalationWorker> _logger;
    private readonly IRedisCache _redis;

    public SlaEscalationWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<SlaRuleSet> rules,
        IRedisCache redis,
        ILogger<SlaEscalationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _rules = rules;
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
        // Simple coordination lock via Redis to avoid duplicate escalations in multi-instance runs.
        var lockKey = "claimpilot:sla:sweep";
        var acquired = await _redis.SetIfNotExistsAsync(lockKey, TimeSpan.FromMinutes(1));
        if (!acquired) return;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var approvals = scope.ServiceProvider.GetRequiredService<IApprovalRepository>();
            var sla = scope.ServiceProvider.GetRequiredService<ISlaPolicy>();
            var approvalService = scope.ServiceProvider.GetRequiredService<IApprovalService>();
            var audit = scope.ServiceProvider.GetRequiredService<IAuditService>();

            var pending = await approvals.QueryAsync(ApprovalStatus.Pending, null, null, ct);
            var now = DateTime.UtcNow;

            foreach (var item in pending)
            {
                var action = sla.EvaluateEscalation(item.CreatedAt, item.ReviewedAt, item.AssignedTo, now);

                switch (action)
                {
                    case "pool":
                        // Move to pool: clear assignment so least-loaded routing picks it up.
                        await approvalService.AssignAsync(item.Id,
                            new AssignRequest("pool", ReviewerId: "sla-worker"), ct);
                        break;

                    case "supervisor":
                        await approvalService.AssignAsync(item.Id,
                            new AssignRequest("supervisor", ReviewerId: "sla-worker"), ct);
                        break;

                    case "director":
                        await approvalService.EscalateAsync(item.Id,
                            new EscalateRequest("sla-worker", Array.Empty<string>(), "Late past SLA deadline; escalated to director."), ct);
                        break;
                }
            }
        }
        finally
        {
            await _redis.DeleteAsync(lockKey);
        }
    }
}