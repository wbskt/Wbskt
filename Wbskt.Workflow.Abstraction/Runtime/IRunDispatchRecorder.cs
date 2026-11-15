namespace Wbskt.Workflow.Abstraction.Runtime;

/// <summary>
/// Records which branches this instance dispatches for a while, so a new leader can tell the Active
/// branches it drives itself from ones an old leader, still stopping, left behind with nothing to run them.
/// </summary>
public interface IRunDispatchRecorder
{
    /// <summary>Starts recording every branch dispatched from now on.</summary>
    void StartRecording();

    /// <summary>Stops recording and returns the ids of the branches dispatched since it started.</summary>
    IReadOnlySet<long> StopRecording();
}
