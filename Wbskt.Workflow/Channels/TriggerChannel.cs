using System.Text.Json;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Channels;

public abstract class TriggerChannel : ITriggerChannel
{
    public abstract string Kind { get; }

    public abstract string CorrelationKey(InboundEvent evt);

    public virtual TriggerRegistrationSpec? Plan(BaseNode node, TriggerPublication publication) => null;

    protected static JsonElement Json<T>(T value) => JsonSerializer.SerializeToElement(value);

    protected static string Required(InboundEvent evt, string field)
    {
        if (!evt.Payload.TryGetValue(field, out JsonElement value))
        {
            throw new InvalidOperationException($"Inbound event payload is missing required field '{field}' for channel '{evt.ChannelKind}'.");
        }

        string? text = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Null => null,
            _ => value.ToString()
        };

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException($"Inbound event payload field '{field}' is empty for channel '{evt.ChannelKind}'.");
        }

        return text;
    }
}
