namespace Wbskt.Workflow.Abstraction.Enums;

/// <summary>
/// What <c>Run_Start</c> checks, under a lock on the trigger and correlation key, before it creates a
/// run. The concurrency policy reads the active runs first; this check makes that read and the insert
/// one step, so two events with the same key cannot both find nothing running (#467).
/// </summary>
public enum ActiveRunGuard
{
    /// <summary>No check: a parallel trigger, or an event with no correlation key.</summary>
    None = 0,

    /// <summary>Refused while any run is Running, Failing or Cancelling: DropIfRunning and Queue.</summary>
    AnyActive = 1,

    /// <summary>Refused while a run is Running or Failing. CancelExisting has just moved the runs it found to Cancelling.</summary>
    NotCancelling = 2
}
