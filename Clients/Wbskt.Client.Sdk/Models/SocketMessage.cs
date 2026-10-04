using System.Text.Json.Serialization;
using Wbskt.Client.Sdk.Internal;

namespace Wbskt.Client.Sdk.Models;

/// <summary>
/// Represents a message exchanged between the client and server.
/// </summary>
public record SocketMessage(
    [property: JsonPropertyName("type")]      string  Type,
    [property: JsonPropertyName("payload")]   object  Payload,
    [property: JsonPropertyName("commandId")] string? CommandId = null
)
{
    /// <summary>
    /// When the device produced the message (device clock). Set by the SDK on application
    /// messages so a reading sent late, after the device was offline, keeps its original time.
    /// A missing or unreadable value is treated as "sent now" by the platform.
    /// </summary>
    [JsonPropertyName("sentAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonConverter(typeof(LenientDateTimeOffsetConverter))]
    public DateTimeOffset? SentAt { get; init; }

    /// <summary>
    /// On a command from the platform: the instant after which it must not be acted on. The SDK
    /// refuses an expired command (answering <c>sys.ack</c> with <c>refused: "expired"</c>) instead
    /// of raising it. Absent means the command does not expire.
    /// </summary>
    [JsonPropertyName("expiresAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonConverter(typeof(LenientDateTimeOffsetConverter))]
    public DateTimeOffset? ExpiresAt { get; init; }
}
