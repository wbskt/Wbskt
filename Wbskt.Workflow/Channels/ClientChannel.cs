using System.Text.Json;
using Wbskt.Protocol;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Triggers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Channels;

/// <summary>A message a client device sent. Keys: <c>client:{clientRef}:{type}</c>, with <c>*</c> as the any-type trigger.</summary>
public sealed class ClientChannel : TriggerChannel
{
    public const string Name = "client";
    public const string AnyType = "*";

    public override string Kind => Name;

    // The client ref is taken as text: a key is compared as a string, so it is never reformatted here.
    public static string Key(string clientRef, string type) => $"{Name}:{clientRef}:{type}";

    // The platform's own messages (state.report, capabilities) reach only the triggers that name
    // them: an any-type trigger means "this device's messages", and would otherwise run on every
    // reconnect and state change.
    public static InboundEvent Event(ClientMessage message, string inboundEventId) => new(
        Name,
        MessageTypes.IsReserved(message.Type)
            ? [Key(message.ClientRefId.ToString(), message.Type)]
            : [Key(message.ClientRefId.ToString(), message.Type), Key(message.ClientRefId.ToString(), AnyType)],
        inboundEventId,
        new Dictionary<string, JsonElement>
        {
            ["messageType"] = Json(message.Type),
            ["clientRefId"] = Json(message.ClientRefId),
            ["clientId"] = Json(message.ClientId),
            ["workspaceId"] = Json(message.WorkspaceId),
            ["payload"] = message.Payload,
            ["sentAt"] = Json(message.SentAt),
            ["receivedAt"] = Json(message.ReceivedAt),
            ["late"] = Json(message.Late)
        },
        default);

    // The message's own type, not "*": an any-type trigger still correlates per message type.
    public override string CorrelationKey(InboundEvent evt) =>
        Key(Required(evt, "clientRefId"), Required(evt, "messageType"));

    public override Task<TriggerRegistrationSpec?> RegisterAsync(BaseNode node, TriggerPublication publication, CancellationToken ct) =>
        Task.FromResult<TriggerRegistrationSpec?>(node is ClientTriggerNode { Config.HoldSeconds: <= 0 } trigger
            ? new TriggerRegistrationSpec(Key(trigger.Config.ClientRef, trigger.Config.Type), trigger.Config.ConcurrencyPolicy, trigger.Config.CorrelationKey)
            {
                Filter = trigger.Config.Filter
            }
            : null);
}

public sealed record ClientMessage(
    Guid ClientRefId,
    int ClientId,
    int WorkspaceId,
    string Type,
    JsonElement Payload,
    DateTime SentAt,
    DateTime ReceivedAt,
    bool Late);
