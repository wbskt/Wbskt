using System.Text.Json;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Channels;

/// <summary>
/// What became of a command a workflow sent: the client acknowledged it, or the platform reported it
/// failed. Only wakes a send-command step waiting for that outcome; nothing registers a trigger on
/// it. Keys: <c>command:{commandId}</c>.
/// </summary>
public sealed class CommandChannel : TriggerChannel
{
    public const string Name = "command";
    public const string Acked = "acked";
    public const string Failed = "failed";

    public override string Kind => Name;

    public static string Key(Guid commandId) => $"{Name}:{commandId}";

    /// <param name="clientRefId">The client the outcome is about: the one that acked, or the command's target for a failure.</param>
    /// <param name="outcome"><see cref="Acked"/> or <see cref="Failed"/>.</param>
    public static InboundEvent Event(Guid commandId, Guid clientRefId, string outcome, string? reason) => new(
        Name,
        [Key(commandId)],
        // One id per client and outcome, so a redelivered ack is dropped by the idempotency claim, and an
        // ack another client sends for the same id cannot claim it before the real one.
        $"{Name}:{commandId}:{clientRefId}:{outcome}",
        new Dictionary<string, JsonElement>
        {
            ["commandId"] = Json(commandId),
            ["clientRefId"] = Json(clientRefId),
            ["outcome"] = Json(outcome),
            ["reason"] = Json(reason)
        },
        default);

    public override string CorrelationKey(InboundEvent evt) => $"{Name}:{Required(evt, "commandId")}";
}
