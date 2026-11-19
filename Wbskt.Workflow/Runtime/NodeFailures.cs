namespace Wbskt.Workflow.Runtime;

/// <summary>
/// The failure a step records when its executor threw something it did not expect. Run history is
/// readable by every member of the workspace, and an unexpected exception's message can name hosts,
/// brokers, tables or procedures, so history gets a fixed message and the engine's log the exception.
/// </summary>
internal static class NodeFailures
{
    public const string CrashCode = "EXECUTOR_CRASH";
    public const string CrashMessage = "The step failed with an internal error. The engine's log has the details.";
}
