using System.Security.Claims;
using ClaimPilot.Application.Interfaces.Review;
using ClaimPilot.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;

namespace ClaimPilot.API.Auth;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class RequireApprovalAuthorityAttribute : Attribute, IAsyncAuthorizationFilter
{
    public ApprovalAction For { get; set; } = ApprovalAction.View;

    public RequireApprovalAuthorityAttribute(ApprovalAction @for) => For = @for;

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var http = context.HttpContext;
        var logger = http.RequestServices.GetRequiredService<ILogger<RequireApprovalAuthorityAttribute>>();

        // 1. Authentication
        if (http.User.Identity is not { IsAuthenticated: true })
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        // 2. Extract caller's roles
        var callerRoles = http.User.Claims
            .Where(c => c.Type == ClaimTypes.Role)
            .Select(c => c.Value)
            .ToList();

        if (callerRoles.Count == 0)
        {
            context.Result = new ForbidResult();
            return;
        }

        // 3. Parse the item ID from the route
        if (!Guid.TryParse(http.Request.RouteValues["approvalItemId"]?.ToString(), out var itemId))
        {
            context.Result = new BadRequestObjectResult(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Invalid or missing approval item identifier."
            });
            return;
        }

        // 4. Load the item
        var queue = http.RequestServices.GetRequiredService<IApprovalQueueReader>();
        var item = await queue.GetAsync(itemId, http.RequestAborted);
        if (item is null)
        {
            context.Result = new NotFoundObjectResult(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Approval item not found."
            });
            return;
        }

        // 5. NEW: Only the assignee can act on Approve/Reject/Edit
        var assigneeRoleName = item.AssignedTo?.ToString();

        if (For is ApprovalAction.Approve or ApprovalAction.Reject or ApprovalAction.Edit)
        {
            if (item.AssignedTo is null)
            {
                logger.LogWarning("Item {ItemId} is unassigned. Denying {Action}.", itemId, For);
                context.Result = new ObjectResult(new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Title = "Item is unassigned.",
                    Detail = "This item has no assignee. Escalate or contact an administrator."
                })
                { StatusCode = StatusCodes.Status409Conflict };
                return;
            }

            if (assigneeRoleName is null || !callerRoles.Contains(assigneeRoleName))
            {
                logger.LogWarning(
                    "Item {ItemId} is assigned to {AssignedTo}. Caller roles: [{Roles}]. Denying {Action}.",
                    itemId, assigneeRoleName, string.Join(",", callerRoles), For);

                context.Result = new ObjectResult(new ProblemDetails
                {
                    Status = StatusCodes.Status403Forbidden,
                    Title = "Forbidden",
                    Detail = $"This item is assigned to a {assigneeRoleName}. Only users with that role can act on it."
                })
                { StatusCode = StatusCodes.Status403Forbidden };
                return;
            }
        }

        // 6. Skip amount check for non-money actions
        if (For is ApprovalAction.View 
                or ApprovalAction.Reject 
                or ApprovalAction.Assign 
                or ApprovalAction.Escalate)
        {
            // Reject still requires assignee check (done above)
            // Assign is supervisor-only (enforced below)
            if (For == ApprovalAction.Assign 
                && !callerRoles.Any(r => r is "Supervisor" or "Director"))
            {
                context.Result = new ForbidResult();
                return;
            }

            return;
        }

        // 7. Amount authority check (for Approve/Edit)
        var authority = http.RequestServices.GetRequiredService<IAuthorityService>();
        var amount = item.ProposedAmount ?? 0m;
        var allowed = await authority.CanApproveAsync(
            http.User, amount, For.ToString(), http.RequestAborted);

        if (allowed)
            return;

        var budget = authority.GetThresholdForUser(http.User);
        logger.LogWarning(
            "Approval authority exceeded. User: {User}. Action: {Action}. Item: {ItemId}. Amount: {Amount}. Threshold: {Threshold}.",
            http.User.Identity.Name, For, itemId, amount, budget);

        context.Result = new ObjectResult(new ProblemDetails
        {
            Status = StatusCodes.Status403Forbidden,
            Title = "Approval authority exceeded.",
            Detail = $"Your approval authority (${budget:N2}) does not cover this amount (${amount:N2}). Please escalate this item to a higher role."
        })
        { StatusCode = StatusCodes.Status403Forbidden };
    }
}