namespace Wbskt.Workflow.Engine.Host.Models;

public sealed class NodeExecutionResult
{
    public List<string> ActivatedPortIds { get; set; } = new();

    public DateTime? WaitUntil { get; set; }

    public bool IsSuccess { get; set; } = true;

    public string? ErrorMessage { get; set; }

    public static NodeExecutionResult Success(string portId) => new() { ActivatedPortIds = { portId } };
    public static NodeExecutionResult Success(IEnumerable<string> portIds) => new() { ActivatedPortIds = portIds.ToList() };
    public static NodeExecutionResult Fail(string error) => new() { IsSuccess = false, ErrorMessage = error };
    public static NodeExecutionResult Wait(DateTime resumeAt) => new() { WaitUntil = resumeAt };
}
