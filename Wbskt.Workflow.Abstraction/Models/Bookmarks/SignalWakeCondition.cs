using System.Text.Json.Serialization;

namespace Wbskt.Workflow.Abstraction.Models.Bookmarks;

public sealed record SignalWakeCondition(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("correlation")] string Correlation
) : WakeCondition;
