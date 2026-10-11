using System.Text.Json;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Channels;

/// <summary>
/// What became of a command a workflow sent: the device acknowledged it, or the platform reported it
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

    /// <param name="outcome"><see cref="Acked"/> or <see cref="Failed"/>.</param>
    public static InboundEvent Event(Guid commandId, string outcome, string? reason) => new(
        Name,
        [Key(commandId)],
        // One id per outcome, so a redelivered ack is dropped by the idempotency claim.
        $"{Name}:{commandId}:{outcome}",
        new Dictionary<string, JsonElement>
        {
            ["commandId"] = Json(commandId),
            ["outcome"] = Json(outcome),
            ["reason"] = Json(reason)
        },
        default);

    public override string CorrelationKey(InboundEvent evt) => $"{Name}:{Required(evt, "commandId")}";
}
