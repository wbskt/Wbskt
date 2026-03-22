using System.Collections.Immutable;

namespace Wbskt.Workflow.Engine.Host.Models;

public sealed class NodeExecutionResult
{
    public IReadOnlyCollection<string> ActivatedPortIds { get; set; } = [];

    public DateTime? WaitUntil { get; set; }

    public bool IsSuccess { get; set; } = true;

    public string? ErrorMessage { get; set; }

    public static NodeExecutionResult Success(string portId) => new() { ActivatedPortIds = [portId] };
    public static NodeExecutionResult Success(IEnumerable<string> portIds) => new() { ActivatedPortIds = portIds.ToImmutableList() };
    public static NodeExecutionResult Fail(string error) => new() { IsSuccess = false, ErrorMessage = error };
    public static NodeExecutionResult Wait(DateTime resumeAt) => new() { WaitUntil = resumeAt };
}
