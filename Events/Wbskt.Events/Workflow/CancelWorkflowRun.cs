using Wbskt.EventBus.Abstractions;

namespace Wbskt.Events.Workflow;

/// <summary>
/// A command to the workflow engine: cancel this run. The engine is the only writer of run state, so
/// the management host sends this rather than cancelling in process. It is consumed from one shared
/// queue, so exactly one engine instance handles each, and handling is idempotent: a run that is
/// already cancelling gets its cleanup again, a terminal run is left alone. Not the audit record of
/// who asked (<see cref="WorkflowRunCancelRequestedEvent"/>), and not itself in the audit log.
/// </summary>
public sealed record CancelWorkflowRun(long RunId, string Reason) : ICommand
{
    public static string QueueName => "workflow-cancel-run";
}
