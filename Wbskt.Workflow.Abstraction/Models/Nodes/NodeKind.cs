namespace Wbskt.Workflow.Abstraction.Models.Nodes;

public static class NodeKind
{
    public const string TriggerDevice = "trigger:device";
    public const string TriggerSchedule = "trigger:schedule";
    public const string TriggerWebhook = "trigger:webhook";
    public const string TriggerManual = "trigger:manual";
    public const string ControlLogic = "control:logic";
    public const string ControlForEach = "control:foreach";
    public const string ControlParallelForEach = "control:parallelForEach";
    public const string ControlJoin = "control:join";
    public const string ControlDelay = "control:delay";
    public const string ControlVariable = "control:variable";
    public const string ControlSubWorkflow = "control:subWorkflow";
    public const string ControlWaitForHttp = "control:waitForHttp";
    public const string ControlAwaitSignal = "control:awaitSignal";
    public const string ControlFailRun = "control:failRun";
    public const string ActionCommand = "action:command";
    public const string ActionEmail = "action:email";
    public const string ActionWebhook = "action:webhook";
    public const string ActionTelegram = "action:telegram";
    public const string ActionToast = "action:toast";
}

