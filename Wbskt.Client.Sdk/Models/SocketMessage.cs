using System.Text.Json.Serialization;

namespace Wbskt.Client.Sdk.Models;

/// <summary>
/// Represents a message exchanged between the client and server.
/// </summary>
public record SocketMessage(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("payload")] object Payload,
    [property: JsonPropertyName("commandId")] string? CommandId = null,
    [property: JsonPropertyName("action")] string? Action = null
);
