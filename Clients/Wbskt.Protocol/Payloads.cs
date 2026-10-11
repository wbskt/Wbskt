using System.Text.Json.Serialization;

namespace Wbskt.Protocol;

/// <summary>
/// The payload of <see cref="MessageTypes.Ping"/>. The device echoes <see cref="Id"/> (and the
/// timestamp, which older servers read) in its <see cref="PongPayload"/>.
/// </summary>
public sealed record PingPayload(
    [property: JsonPropertyName("timestamp")] DateTime Timestamp,
    [property: JsonPropertyName("id")] string? Id = null);

/// <summary>
/// The payload of <see cref="MessageTypes.Pong"/>. The server measures the round trip on its own
/// clock and matches the ping by <see cref="Id"/>; it does not trust <see cref="OriginalTimestamp"/>.
/// </summary>
public sealed record PongPayload(
    [property: JsonPropertyName("originalTimestamp")] DateTime OriginalTimestamp,
    [property: JsonPropertyName("id")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Id = null);

/// <summary>
/// The payload of <see cref="MessageTypes.Ack"/>. A device that will not act on a command (it
/// arrived past its expiresAt) still answers, with <see cref="Refused"/> set and the command's
/// <see cref="Type"/>, so the sender sees a failure rather than silence.
/// </summary>
public sealed record AckPayload(
    [property: JsonPropertyName("commandId")] string CommandId,
    [property: JsonPropertyName("type")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Type = null,
    [property: JsonPropertyName("refused")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Refused = null)
{
    /// <summary>The <see cref="Refused"/> value for a command that arrived past its expiresAt.</summary>
    public const string Expired = "expired";
}
