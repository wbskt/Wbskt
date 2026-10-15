using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
using Wbskt.Workflow.Abstraction.Models.Nodes.Triggers;
using Wbskt.Workflow.Abstraction.Models.Triggers;

namespace Wbskt.Workflow.Abstraction.Models.Nodes;

/// <summary>
/// The one list of node kinds the definition model can express. The JSON converter, the validator's
/// kind and port checks, the built-in price list and <see cref="NodeKind.All"/> all read it.
/// </summary>
/// <remarks>
/// Executors are registered separately, because which class runs a kind is a runtime concern this
/// assembly does not see; an architecture test pins that every descriptor has exactly one executor
/// and every executor has a descriptor. The five trigger kinds share one executor class
/// (<c>PassthroughTriggerExecutor</c>), registered once per kind.
///
/// <para>Prices, roughly: work that leaves the process 5, in-process messaging 1, bookkeeping 0.1.
/// The rationale for that shape is on <c>CreditCostOptions</c>.</para>
/// </remarks>
public static class NodeKinds
{
    private static readonly PortDefinition In = new() { PortId = "in", Direction = PortDirection.Input, Label = "In" };
    private static readonly PortDefinition Default = Output("default", "Default");
    private static readonly PortDefinition True = Output("true", "True");
    private static readonly PortDefinition False = Output("false", "False");
    private static readonly PortDefinition Body = Output("body", "Body");
    private static readonly PortDefinition Done = Output("done", "Done");
    private static readonly PortDefinition Empty = Output("empty", "Empty");
    private static readonly PortDefinition Error = Output("error", "Error");
    private static readonly PortDefinition Timeout = Output("timeout", "Timeout");
    private static readonly PortDefinition Acked = Output("acked", "Acked");
    private static readonly PortDefinition Failed = Output("failed", "Failed");

    private static readonly IReadOnlyCollection<PortDefinition> TriggerPorts = [Default];
    private static readonly IReadOnlyCollection<PortDefinition> PassThroughPorts = [In, Default];
    private static readonly IReadOnlyCollection<PortDefinition> TerminalPorts = [In];

    public static readonly IReadOnlyList<NodeKindDescriptor> All =
    [
        // A trigger only hands the inbound payload on, so it is free: every run starts at one, and
        // priced at the default every run paid a full credit before its first real node.
        TriggerKind(NodeKind.TriggerClient, typeof(ClientTriggerNode), typeof(ClientTriggerConfig)),
        TriggerKind(NodeKind.TriggerClientPresence, typeof(ClientPresenceTriggerNode), typeof(ClientPresenceTriggerConfig)),
        TriggerKind(NodeKind.TriggerSchedule, typeof(ScheduleTriggerNode), typeof(ScheduleTriggerConfig)),
        TriggerKind(NodeKind.TriggerWebhook, typeof(WebhookTriggerNode), typeof(WebhookTriggerConfig)),
        TriggerKind(NodeKind.TriggerManual, typeof(ManualTriggerNode), typeof(ManualTriggerConfig)),

        // Bookkeeping: a row write at most, usually a dictionary write.
        ControlKind(NodeKind.ControlLogic, typeof(LogicGateNode), typeof(LogicGateConfig), 0.1m, Fixed([In, True, False])),
        ControlKind(NodeKind.ControlForEach, typeof(ForEachNode), typeof(ForEachConfig), 0.1m, Fixed([In, Body, Done])),
        ControlKind(NodeKind.ControlParallelForEach, typeof(ParallelForEachNode), typeof(ParallelForEachConfig), 0.1m, Fixed([In, Body, Empty])),
        ControlKind(NodeKind.ControlJoin, typeof(JoinNode), typeof(JoinConfig), 0.1m, Fixed(PassThroughPorts)),
        ControlKind(NodeKind.ControlFork, typeof(ForkNode), typeof(ForkConfig), 0.1m, ForkPorts),
        ControlKind(NodeKind.ControlVariable, typeof(VariableNode), typeof(VariableConfig), 0.1m, Fixed(PassThroughPorts)),
        ControlKind(NodeKind.ControlEnd, typeof(EndNode), typeof(EndConfig), 0.1m, Fixed(TerminalPorts)),
        ControlKind(NodeKind.ControlFailRun, typeof(FailRunNode), typeof(FailRunConfig), 0.1m, Fixed(TerminalPorts)),

        // Parking is cheap to enter; the waiting itself costs nothing, since nothing is running.
        ControlKind(NodeKind.ControlDelay, typeof(DelayNode), typeof(DelayConfig), 0.1m, Fixed(PassThroughPorts)),
        // WaitForHttp always has a mandatory Ttl, so the timeout port is always present.
        ControlKind(NodeKind.ControlWaitForHttp, typeof(WaitForHttpNode), typeof(WaitForHttpConfig), 0.1m, Fixed([In, Default, Timeout])),
        ControlKind(NodeKind.ControlAwaitSignal, typeof(AwaitSignalNode), typeof(AwaitSignalConfig), 0.1m, AwaitSignalPorts),

        // A child run is charged against its own budget, so the parent pays only for dispatching it.
        ControlKind(NodeKind.ControlSubWorkflow, typeof(SubWorkflowNode), typeof(SubWorkflowConfig), 1.0m, Fixed(PassThroughPorts)),

        // Crosses the bus but stays inside the platform.
        ActionKind(NodeKind.ActionClientMessage, typeof(SendClientMessageNode), typeof(SendClientMessageConfig), 1.0m, ClientMessagePorts),
        ActionKind(NodeKind.ActionToast, typeof(ToastNotificationNode), typeof(ToastConfig), 0.5m, Fixed(PassThroughPorts)),

        // Leaves the process, over somebody else's network, and can be retried.
        ActionKind(NodeKind.ActionWebhook, typeof(WebhookNotificationNode), typeof(WebhookNotificationConfig), 5.0m, Fixed([In, Default, Error])),
        ActionKind(NodeKind.ActionEmail, typeof(EmailNotificationNode), typeof(EmailConfig), 5.0m, Fixed(PassThroughPorts)),
        ActionKind(NodeKind.ActionTelegram, typeof(TelegramNotificationNode), typeof(TelegramConfig), 5.0m, Fixed(PassThroughPorts)),
        ActionKind(NodeKind.ActionDiscord, typeof(DiscordNotificationNode), typeof(DiscordConfig), 5.0m, Fixed(PassThroughPorts)),
        ActionKind(NodeKind.ActionNtfy, typeof(NtfyNotificationNode), typeof(NtfyConfig), 5.0m, Fixed(PassThroughPorts))
    ];

    private static readonly IReadOnlyDictionary<string, NodeKindDescriptor> ByKind =
        All.ToDictionary(descriptor => descriptor.Kind, StringComparer.Ordinal);

    /// <summary>Looks up a kind's descriptor; false for a kind the model cannot express.</summary>
    public static bool TryGet(string? kind, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out NodeKindDescriptor? descriptor)
    {
        if (kind is null)
        {
            descriptor = null;
            return false;
        }

        return ByKind.TryGetValue(kind, out descriptor);
    }

    private static IReadOnlyCollection<PortDefinition> ForkPorts(BaseNode node)
    {
        // One output per named branch; the names are the port ids the branches leave by.
        var ports = new List<PortDefinition> { In };
        if (node is ForkNode { Config: not null } fork)
        {
            ports.AddRange(fork.Config.Branches.Select(branch => Output(branch, branch)));
        }

        return ports;
    }

    private static IReadOnlyCollection<PortDefinition> AwaitSignalPorts(BaseNode node) =>
        node is AwaitSignalNode { Config.Ttl: not null } ? [In, Default, Timeout] : PassThroughPorts;

    // A send-command step that waits for the device leaves by what became of the command.
    private static IReadOnlyCollection<PortDefinition> ClientMessagePorts(BaseNode node) =>
        node is SendClientMessageNode { Config.AwaitAck: true } ? [In, Acked, Failed, Timeout] : PassThroughPorts;

    private static NodeKindDescriptor TriggerKind(string kind, Type model, Type config) =>
        new(kind, NodeCategory.Trigger, model, config, 0m, Fixed(TriggerPorts));

    private static NodeKindDescriptor ControlKind(string kind, Type model, Type? config, decimal cost, Func<BaseNode, IReadOnlyCollection<PortDefinition>> ports) =>
        new(kind, NodeCategory.Control, model, config, cost, ports);

    private static NodeKindDescriptor ActionKind(string kind, Type model, Type config, decimal cost, Func<BaseNode, IReadOnlyCollection<PortDefinition>> ports) =>
        new(kind, NodeCategory.Action, model, config, cost, ports);

    private static Func<BaseNode, IReadOnlyCollection<PortDefinition>> Fixed(IReadOnlyCollection<PortDefinition> ports) => _ => ports;

    private static PortDefinition Output(string id, string label) => new() { PortId = id, Direction = PortDirection.Output, Label = label };
}
