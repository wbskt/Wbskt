using System.Text.Json.Serialization;
using Webskt.Workflow.Abstraction.Models.Nodes.Actions;
using Webskt.Workflow.Abstraction.Models.Nodes.Controls;
using Webskt.Workflow.Abstraction.Models.Nodes.Triggers;

namespace Webskt.Workflow.Abstraction.Models.Nodes;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
// Triggers
[JsonDerivedType(typeof(DeviceTriggerNode), "trigger:device")]
[JsonDerivedType(typeof(TimerScheduleNode), "trigger:timer")]
// Actions
[JsonDerivedType(typeof(EmailNotificationNode), "action:email")]
[JsonDerivedType(typeof(SendCommandActionNode), "action:command")]
[JsonDerivedType(typeof(TelegramNotificationNode), "action:telegram")]
[JsonDerivedType(typeof(ToastNotificationNode), "action:toast")]
[JsonDerivedType(typeof(WebhookNotificationNode), "action:webhook")]
// Controls
[JsonDerivedType(typeof(DelayNode), "control:delay")]
[JsonDerivedType(typeof(LogicGateNode), "control:logic")]
[JsonDerivedType(typeof(LoopNode), "control:loop")]
[JsonDerivedType(typeof(VariableNode), "control:variable")]
public abstract class BaseNode
{
    public Guid NodeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public abstract List<PortDefinition> Ports { get; set; }
}