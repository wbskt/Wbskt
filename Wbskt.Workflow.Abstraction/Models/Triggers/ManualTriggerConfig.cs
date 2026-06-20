using System.Text.Json.Serialization;

namespace Wbskt.Workflow.Abstraction.Models.Triggers;

public sealed record ManualTriggerConfig
{
    [JsonPropertyName("description")]
    public string? Description { get; init; } = null;

}

