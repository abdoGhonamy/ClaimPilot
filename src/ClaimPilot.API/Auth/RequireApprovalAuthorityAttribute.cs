using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

using ClaimPilot.Application.Interfaces.Review;

namespace ClaimPilot.API.Auth;

[AttributeUsage(AttributeTargets.Method)]
public sealed class RequireApprovalAuthorityAttribute : AuthorizeAttribute, IAsyncAuthorizationFilter
{
    public ApprovalAction For { get; set; }

    public RequireApprovalAuthorityAttribute(ApprovalAction @for) => For = @for;

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var http = context.HttpContext;

        if (http.User.Identity is not { IsAuthenticated: true })
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        var callerRoles = http.User.Claims
            .Where(c => c.Type == System.Security.Claims.ClaimTypes.Role)
            .Select(c => c.Value)
            .ToList();

        if (callerRoles.All(r => r is not ("Adjuster" or "Supervisor" or "Director")))
        {
            context.Result = new ForbidResult();
            return;
        }

        if (For is ApprovalAction.View or ApprovalAction.Reject or ApprovalAction.Assign or ApprovalAction.Escalate)
        {
            return;
        }

        if (!Guid.TryParse(http.Request.RouteValues["approvalItemId"]?.ToString(), out var itemId))
        {
            context.Result = new BadRequestObjectResult(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Invalid or missing approval item identifier.",
                Detail = "The approvalItemId route value must be a valid GUID."
            });
            return;
        }

        var queue = http.RequestServices.GetRequiredService<IApprovalQueueReader>();
        var item = await queue.GetAsync(itemId, http.RequestAborted);
        if (item is null)
        {
            context.Result = new NotFoundObjectResult(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Approval item not found.",
                Detail = "No approval item exists for the supplied approvalItemId."
            });
            return;
        }

        var authority = http.RequestServices.GetRequiredService<IAuthorityService>();
        var amount = item.ProposedAmount ?? 0m;
        var allowed = await authority.CanApproveAsync(http.User, amount, For.ToString(), http.RequestAborted);
        if (allowed)
        {
            return;
        }

        var budget = authority.GetThresholdForUser(http.User);
        context.Result = new ObjectResult(new ProblemDetails
        {
            Status = StatusCodes.Status403Forbidden,
            Title = "Approval authority exceeded.",
            Detail = string.Format(
                "Your approval authority (${0:N2}) does not cover this amount (${1:N2}). Please escalate this item to a higher role.",
                budget, amount)
        })
        {
            StatusCode = StatusCodes.Status403Forbidden
        };
    }
}