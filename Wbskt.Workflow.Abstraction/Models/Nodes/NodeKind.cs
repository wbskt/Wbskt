namespace Wbskt.Workflow.Abstraction.Models.Nodes;

public static class NodeKind
{
    public const string TriggerClient = "trigger:client";
    public const string TriggerSchedule = "trigger:schedule";
    public const string TriggerWebhook = "trigger:webhook";
    public const string TriggerManual = "trigger:manual";
    public const string ControlLogic = "control:logic";
    public const string ControlForEach = "control:foreach";
    public const string ControlParallelForEach = "control:parallelForEach";
    public const string ControlJoin = "control:join";
    public const string ControlFork = "control:fork";
    public const string ControlDelay = "control:delay";
    public const string ControlVariable = "control:variable";
    public const string ControlSubWorkflow = "control:subWorkflow";
    public const string ControlWaitForHttp = "control:waitForHttp";
    public const string ControlAwaitSignal = "control:awaitSignal";
    public const string ControlFailRun = "control:failRun";
    public const string ControlEnd = "control:end";
    public const string ActionClientMessage = "action:clientMessage";
    public const string ActionEmail = "action:email";
    public const string ActionWebhook = "action:webhook";
    public const string ActionTelegram = "action:telegram";
    public const string ActionToast = "action:toast";

    /// <summary>Every kind the definition model can express.</summary>
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        TriggerClient, TriggerSchedule, TriggerWebhook, TriggerManual,
        ControlLogic, ControlForEach, ControlParallelForEach, ControlJoin, ControlFork,
        ControlDelay, ControlVariable, ControlSubWorkflow, ControlWaitForHttp, ControlAwaitSignal,
        ControlFailRun, ControlEnd,
        ActionClientMessage, ActionEmail, ActionWebhook, ActionTelegram, ActionToast
    };

    /// <summary>
    /// Kinds the model can express but the engine cannot execute. The validator rejects them at
    /// publish, so an author gets an error instead of a run that fails partway through.
    ///
    /// Keep this in sync with the executors: when one of these is implemented, remove it here.
    /// <c>NodeExecutorRegistryTests</c> pins the relationship between this set and the registry.
    /// </summary>
    public static readonly IReadOnlySet<string> NotYetImplemented = new HashSet<string>(StringComparer.Ordinal)
    {
        ActionEmail,
        ActionTelegram
    };

    /// <summary>Kinds that can actually run - what a published definition may contain.</summary>
    public static IEnumerable<string> Executable => All.Where(kind => !NotYetImplemented.Contains(kind));
}

