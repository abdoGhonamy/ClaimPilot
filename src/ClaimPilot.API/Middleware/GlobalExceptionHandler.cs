using System.Text.Json;

using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

using ClaimPilot.Domain.Exceptions;

namespace ClaimPilot.API.Middleware;

/// <summary>
/// Translates domain and unexpected exceptions into RFC 7807 ProblemDetails
/// responses without leaking internals to the wire.
/// </summary>
public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) => _logger = logger;

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken ct)
    {
        var (status, code, message) = exception switch
        {
            PolicyVersionNotFoundException ex => (StatusCodes.Status404NotFound, "policy_version_not_found", ex.Message),
            InsufficientInformationException ex => (StatusCodes.Status422UnprocessableEntity, "insufficient_information", ex.Message),
            GatedWriteException ex => (StatusCodes.Status403Forbidden, "gated_write", ex.Message),
            ValidationException ex => (StatusCodes.Status400BadRequest, "validation_error", ex.Message),
            DomainException ex => (StatusCodes.Status400BadRequest, "domain_error", ex.Message),
            _ => (StatusCodes.Status500InternalServerError, "internal_error", "An unexpected error occurred.")
        };

        if (status >= 500)
        {
            _logger.LogError(exception, "Unhandled exception in {Path}", httpContext.Request.Path);
        }
        else
        {
            _logger.LogWarning(exception, "Handled exception in {Path}: {Message}", httpContext.Request.Path, message);
        }

        httpContext.Response.StatusCode = status;
        httpContext.Response.ContentType = "application/problem+json";

        var problem = new ProblemDetails
        {
            Status = status,
            Title = code,
            Detail = message,
            Instance = httpContext.Request.Path
        };

        await httpContext.Response.WriteAsync(JsonSerializer.Serialize(problem), ct);
        return true;
    }
}