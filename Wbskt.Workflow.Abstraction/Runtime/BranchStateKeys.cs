namespace Wbskt.Workflow.Abstraction.Runtime;

/// <summary>
/// The names in a branch's local state that the engine writes and reads itself. A Local
/// <c>control:variable</c> writes straight into the same dictionary, so a variable called
/// <c>trigger</c> or <c>__join_token</c> silently overwrote the trigger payload or stranded a cohort
/// at its Join; the validator rejects those names at publish instead.
/// </summary>
/// <remarks>
/// This is the validator's list, not the engine's source of truth: the executors and the resumer
/// still own their key literals. When one of them adds a key it must be added here as well, unless it
/// starts with <see cref="EnginePrefix"/>, which is reserved as a whole.
/// </remarks>
public static class BranchStateKeys
{
    /// <summary>Every name starting with this is the engine's, including ones added later.</summary>
    public const string EnginePrefix = "__";

    /// <summary>The inbound event's payload, written when the run starts.</summary>
    public const string Trigger = "trigger";

    public const string Wake = "__wake";
    public const string WakeNode = "__wake_node";
    public const string JoinToken = "__join_token";
    public const string JoinOuter = "__join_outer";
    public const string JoinIndex = "__join_index";
    public const string AwaitSignal = "__await_signal";
    public const string AwaitSignalUntil = "__await_signal_until";
    public const string HttpWait = "__http_wait";
    public const string HttpDeadline = "__http_deadline";
    public const string SubWorkflow = "__subworkflow";
    public const string CommandAwaiting = "__command_awaiting";
    public const string DelayUntil = "__delay_until";

    /// <summary>The attempt a node parked for a retry backoff runs next when its timer wakes it.</summary>
    public const string RetryAttempt = "__retry_attempt";

    /// <summary>The counts a Join continuation carries; written without the engine prefix.</summary>
    public const string JoinContributed = "joinContributed";
    public const string JoinSucceeded = "joinSucceeded";
    public const string JoinFailed = "joinFailed";

    /// <summary>A sequential ForEach keeps its position under <c>__foreach:{nodeId}:index</c>.</summary>
    public const string ForEachIteratorPrefix = "__foreach:";

    /// <summary>The reserved names that are listed one by one.</summary>
    public static readonly IReadOnlySet<string> Names = new HashSet<string>(StringComparer.Ordinal)
    {
        Trigger,
        Wake,
        WakeNode,
        JoinToken,
        JoinOuter,
        JoinIndex,
        AwaitSignal,
        AwaitSignalUntil,
        HttpWait,
        HttpDeadline,
        SubWorkflow,
        CommandAwaiting,
        DelayUntil,
        RetryAttempt,
        JoinContributed,
        JoinSucceeded,
        JoinFailed
    };

    /// <summary>Whether <paramref name="name"/> is one the engine keeps for itself.</summary>
    public static bool IsReserved(string name)
    {
        // The ForEach iterator prefix is covered by the engine prefix.
        return name.StartsWith(EnginePrefix, StringComparison.Ordinal) || Names.Contains(name);
    }
}
