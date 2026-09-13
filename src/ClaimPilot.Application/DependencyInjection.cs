using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

using ClaimPilot.Application.Interfaces;
using ClaimPilot.Application.Interfaces.Adjudication;
using ClaimPilot.Application.Interfaces.Assignment;
using ClaimPilot.Application.Interfaces.Orchestration;
using ClaimPilot.Application.Interfaces.Review;
using ClaimPilot.Application.Services;
using ClaimPilot.Application.Services.Agents;

namespace ClaimPilot.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<OrchestratorOptions>(configuration.GetSection("Orchestrator"));
        services.Configure<AssignmentOptions>(configuration.GetSection("Assignment"));
        services.Configure<SlaRuleSet>(configuration.GetSection("Sla"));
        services.Configure<ApprovalAuthorityOptions>(configuration.GetSection(ApprovalAuthorityOptions.SectionName));

        services.AddSingleton<OrchestrationEventSink>();

        services.AddSingleton<IAdjudicationEngine, DeterministicAdjudicationEngine>();
        services.AddSingleton<IPriorityCalculator, PriorityCalculator>();

        services.AddScoped<ISlaPolicy>(sp =>
        {
            var settings = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<SlaRuleSet>>();
            return new DefaultSlaPolicy(settings.Value);
        });
        services.AddScoped<IAssignmentService, AssignmentService>();
        services.AddScoped<IApprovalService, ApprovalService>();
        services.AddScoped<IAuthorityService, AuthorityService>();
        services.AddScoped<IReviewStatisticsService, ReviewStatisticsService>();
        services.AddScoped<AskService>();

        services.AddScoped<CoverageMatcherAgent>();
        services.AddScoped<ExclusionAnalystAgent>();
        services.AddScoped<AnomalyDetectorAgent>();
        services.AddScoped<AdjudicationDrafterAgent>();
        services.AddScoped<IClaimsOrchestrator, SupervisorOrchestrator>();

        return services;
    }
}