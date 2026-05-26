using System.Text.Json.Serialization;

namespace Wbskt.Workflow.Abstraction.Models.Bookmarks;

public sealed record TimerWakeCondition(
    [property: JsonPropertyName("at")] DateTime At
) : WakeCondition;
