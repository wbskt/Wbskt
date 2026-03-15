using System.Text.Json.Serialization;

namespace Wbskt.Client.Windows.Models;

/// <summary>
/// Pure-data DTO used exclusively for JSON serialization/deserialization.
/// Convert to/from <see cref="CommandMapping"/> via the extension methods below.
/// </summary>
public record CommandMappingDto(
    [property: JsonPropertyName("id")]           Guid   Id,
    [property: JsonPropertyName("commandName")]  string CommandName,
    [property: JsonPropertyName("friendlyName")] string FriendlyName,
    [property: JsonPropertyName("actionType")]   string ActionType,
    [property: JsonPropertyName("isEnabled")]    bool   IsEnabled,
    [property: JsonPropertyName("createdAt")]    DateTime CreatedAt,
    [property: JsonPropertyName("parameters")]   Dictionary<string, string> Parameters
);