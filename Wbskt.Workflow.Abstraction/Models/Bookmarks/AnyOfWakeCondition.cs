using System.Text.Json.Serialization;

namespace Wbskt.Workflow.Abstraction.Models.Bookmarks;

public sealed record AnyOfWakeCondition(
    [property: JsonPropertyName("conditions")] IReadOnlyCollection<WakeCondition> Conditions
) : WakeCondition;
