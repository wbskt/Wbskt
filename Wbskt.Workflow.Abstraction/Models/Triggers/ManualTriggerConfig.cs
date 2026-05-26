using System.Text.Json.Serialization;

namespace Wbskt.Workflow.Abstraction.Models.Triggers;

public sealed record ManualTriggerConfig(
    [property: JsonPropertyName("description")] string? Description = null
);
