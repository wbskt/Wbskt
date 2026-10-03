using System.Text.Json;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Runtime;

internal sealed class CorrelationKeyResolver : ICorrelationKeyResolver
{
    public string Resolve(InboundEvent evt)
    {
        return evt.ChannelKind switch
        {
            "client" => $"client:{GetRequiredString(evt, "clientRefId")}:{GetRequiredString(evt, "messageType")}",
            "presence" => $"presence:{GetRequiredString(evt, "clientRefId")}:{GetRequiredString(evt, "state")}",
            "schedule" => $"schedule:{GetRequiredString(evt, "scheduledFireId")}",
            "webhook" => $"webhook:{GetRequiredString(evt, "workspaceRefId")}:{GetRequiredString(evt, "webhookPath")}",
            "manual" => $"manual:{GetRequiredString(evt, "workflowDefinitionRefId")}",
            "signal" => $"signal:{GetRequiredString(evt, "signalName")}:{GetRequiredString(evt, "scopeRunRefId")}",
            "child-completed" => $"child-completed:{GetRequiredString(evt, "childRunRefId")}",
            "http-wake" => $"http-wake:{GetRequiredString(evt, "wakeToken")}",

            // Inbound channels that are valid but currently have no trigger-registration
            // type (e.g. client-connected, client-registered). Fall back to the
            // correlation key supplied by the inbound adapter so dispatch resolves to
            // NoRegistration gracefully instead of faulting the message.
            _ => evt.CorrelationKey ?? string.Empty
        };
    }

    private static string GetRequiredString(InboundEvent evt, string fieldName)
    {
        if (!evt.Payload.TryGetValue(fieldName, out JsonElement value))
        {
            throw new InvalidOperationException($"Inbound event payload is missing required field '{fieldName}' for channel '{evt.ChannelKind}'.");
        }

        string? stringValue = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Null => null,
            _ => value.ToString()
        };

        if (string.IsNullOrWhiteSpace(stringValue))
        {
            throw new InvalidOperationException($"Inbound event payload field '{fieldName}' is empty for channel '{evt.ChannelKind}'.");
        }

        return stringValue;
    }
}
