using System.Text.Json.Serialization;

namespace Wbskt.Workflow.Abstraction.Models.Bookmarks;

public sealed record InboundWakeCondition(
    [property: JsonPropertyName("deviceRefId")] string DeviceRefId,
    [property: JsonPropertyName("propertyName")] string? PropertyName
) : WakeCondition;
