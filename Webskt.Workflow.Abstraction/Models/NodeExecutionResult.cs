namespace Webskt.Workflow.Abstraction.Models;

public sealed class NodeExecutionResult
{
    /// <summary>
    /// The Port IDs that should be triggered next (e.g., "Match", "Out").
    /// If empty, the branch terminates here.
    /// </summary>
    public List<string> ActivatedPortIds { get; set; } = new();

    /// <summary>
    /// If the node is a Delay node, this tells the engine when to resume.
    /// This only affects the specific pointer (branch) that hit the delay.
    /// </summary>
    public DateTime? WaitUntil { get; set; }

    /// <summary>
    /// True if the node executed successfully.
    /// </summary>
    public bool IsSuccess { get; set; } = true;

    public string? ErrorMessage { get; set; }

    public static NodeExecutionResult Success(string portId) => new() { ActivatedPortIds = { portId } };
    public static NodeExecutionResult Success(IEnumerable<string> portIds) => new() { ActivatedPortIds = portIds.ToList() };
    public static NodeExecutionResult Fail(string error) => new() { IsSuccess = false, ErrorMessage = error };
    public static NodeExecutionResult Wait(DateTime resumeAt) => new() { WaitUntil = resumeAt };
}
