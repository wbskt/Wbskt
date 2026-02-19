using System.Text.Json.Serialization;

namespace Webskt.Socket.Host.Models;

public record SocketMessage(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("payload")] object Payload,
    [property: JsonPropertyName("commandId")] string? CommandId = null,
    [property: JsonPropertyName("action")] string? Action = null
);
