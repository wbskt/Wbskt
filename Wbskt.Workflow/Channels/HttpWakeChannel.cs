using System.Text.Json;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Channels;

/// <summary>
/// A call to a Wait For HTTP node's wake URL. Only wakes parked branches; nothing registers a trigger
/// on it. Keys: <c>http-wake:{token}</c>.
/// </summary>
public sealed class HttpWakeChannel : TriggerChannel
{
    public const string Name = "http-wake";

    public override string Kind => Name;

    public static string Key(string token) => $"{Name}:{token}";

    /// <param name="deliveryId">What names this call, so a redelivery of it is the same event; a fresh id when omitted.</param>
    public static InboundEvent Event(string token, JsonElement body, string? deliveryId = null) => new(
        Name,
        [Key(token)],
        $"{Name}:{token}:{deliveryId ?? Guid.NewGuid().ToString()}",
        new Dictionary<string, JsonElement>
        {
            ["wakeToken"] = Json(token),
            ["body"] = body
        },
        default);

    public override string CorrelationKey(InboundEvent evt) => Key(Required(evt, "wakeToken"));
}
