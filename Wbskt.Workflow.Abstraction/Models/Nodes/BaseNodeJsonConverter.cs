using System.Text.Json;
using System.Text.Json.Serialization;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
using Wbskt.Workflow.Abstraction.Models.Nodes.Triggers;

namespace Wbskt.Workflow.Abstraction.Models.Nodes;

public sealed class BaseNodeJsonConverter : JsonConverter<BaseNode>
{
    public override BaseNode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        var kind = root.GetProperty("kind").GetString();
        var nodeType = ResolveNodeType(kind);
        return (BaseNode)root.Deserialize(nodeType, options)!;
    }

    public override void Write(Utf8JsonWriter writer, BaseNode value, JsonSerializerOptions options)
    {
        var type = value.GetType();
        var json = JsonSerializer.Serialize(value, type, options);
        using var doc = JsonDocument.Parse(json);

        writer.WriteStartObject();
        writer.WriteString("kind", value.Kind);

        foreach (var property in doc.RootElement.EnumerateObject())
        {
            if (property.NameEquals("kind"))
            {
                continue;
            }

            property.WriteTo(writer);
        }

        writer.WriteEndObject();
    }

    private static Type ResolveNodeType(string? kind) => kind switch
    {
        NodeKind.ControlLogic => typeof(LogicGateNode),
        NodeKind.ControlDelay => typeof(DelayNode),
        NodeKind.ControlForEach => typeof(ForEachNode),
        NodeKind.ControlParallelForEach => typeof(ParallelForEachNode),
        NodeKind.ControlJoin => typeof(JoinNode),
        NodeKind.ControlVariable => typeof(VariableNode),
        NodeKind.ControlSubWorkflow => typeof(SubWorkflowNode),
        NodeKind.ControlWaitForHttp => typeof(WaitForHttpNode),
        NodeKind.ControlAwaitSignal => typeof(AwaitSignalNode),
        NodeKind.ControlFailRun => typeof(FailRunNode),
        NodeKind.ControlEnd => typeof(EndNode),
        NodeKind.TriggerDevice => typeof(DeviceTriggerNode),
        NodeKind.TriggerSchedule => typeof(ScheduleTriggerNode),
        NodeKind.TriggerWebhook => typeof(WebhookTriggerNode),
        NodeKind.TriggerManual => typeof(ManualTriggerNode),
        NodeKind.ActionCommand => typeof(SendCommandActionNode),
        NodeKind.ActionEmail => typeof(EmailNotificationNode),
        NodeKind.ActionWebhook => typeof(WebhookNotificationNode),
        NodeKind.ActionTelegram => typeof(TelegramNotificationNode),
        NodeKind.ActionToast => typeof(ToastNotificationNode),
        _ => throw new JsonException($"Unsupported node kind '{kind}'.")
    };
}
