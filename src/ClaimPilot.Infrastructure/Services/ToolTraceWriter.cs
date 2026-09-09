using ClaimPilot.Application.Interfaces.Trace;

namespace ClaimPilot.Infrastructure.Services;

/// <summary>Writes tool invocations into the per-run trace.</summary>
public sealed class ToolTraceWriter : IToolTraceWriter
{
    private readonly ITraceService _trace;

    public ToolTraceWriter(ITraceService trace)
    {
        _trace = trace;
    }

    public Task WriteToolAsync(string runId, string tool, string input, string output,
        string? correlationId, CancellationToken ct = default)
        => _trace.WriteAsync(runId, "ToolCall", tool, entityId: runId, before: input, after: output,
            correlationId: correlationId, ct: ct);
}