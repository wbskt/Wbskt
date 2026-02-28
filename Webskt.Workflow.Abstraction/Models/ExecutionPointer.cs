using Webskt.Workflow.Abstraction.Enums;

namespace Webskt.Workflow.Abstraction.Models;

public sealed class ExecutionPointer
{
    public Guid PointerId { get; set; } = Guid.NewGuid();
    
    /// <summary>
    /// The node currently being executed by this branch.
    /// </summary>
    public Guid NodeId { get; set; }

    /// <summary>
    /// Tracks if this specific branch is running or waiting.
    /// </summary>
    public ExecutionStatus Status { get; set; } = ExecutionStatus.Active;

    /// <summary>
    /// Used for DelayNodes to know when to wake up.
    /// </summary>
    public DateTime? ResumeAt { get; set; }

    /// <summary>
    /// If this branch fails, store the error here.
    /// </summary>
    public string? ErrorMessage { get; set; }
}
