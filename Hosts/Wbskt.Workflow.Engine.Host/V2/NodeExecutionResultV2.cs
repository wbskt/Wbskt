namespace Wbskt.Workflow.Engine.Host.V2;

/// <summary>
/// The result returned by a V2 node executor after it finishes running.
/// </summary>
public sealed class NodeExecutionResultV2
{
    /// <summary>The port IDs that should be followed to reach the next node(s).</summary>
    public IReadOnlyCollection<string> ActivatedPortIds { get; init; } = [];

    /// <summary>
    /// Optional value produced by this node that downstream nodes can read via $output.
    /// </summary>
    public object? Output { get; init; }

    /// <summary>
    /// If set the branch will wait for this duration before continuing.
    /// The branch is NOT suspended – it holds its goroutine for the delay.
    /// Use a persisted timer for long delays instead.
    /// </summary>
    public TimeSpan? Delay { get; init; }

    public bool IsSuccess { get; init; } = true;
    public string? ErrorMessage { get; init; }

    // ── Factory helpers ───────────────────────────────────────────────────────

    public static NodeExecutionResultV2 Success(string portId, object? output = null) =>
        new() { ActivatedPortIds = [portId], Output = output };

    public static NodeExecutionResultV2 Success(IEnumerable<string> portIds, object? output = null) =>
        new() { ActivatedPortIds = portIds.ToArray(), Output = output };

    public static NodeExecutionResultV2 Fail(string error) =>
        new() { IsSuccess = false, ErrorMessage = error };

    public static NodeExecutionResultV2 Wait(TimeSpan delay) =>
        new() { Delay = delay };
}

