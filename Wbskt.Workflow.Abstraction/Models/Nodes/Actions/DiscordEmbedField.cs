using System.Text.Json.Serialization;
using Wbskt.Workflow.Abstraction.Templates;

namespace Wbskt.Workflow.Abstraction.Models.Nodes.Actions;

public sealed record DiscordEmbedField
{
    [Templated]
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [Templated]
    [JsonPropertyName("value")]
    public required string Value { get; init; }

    /// <summary>When true, Discord may lay the field out beside its neighbours.</summary>
    [JsonPropertyName("inline")]
    public bool Inline { get; init; }
}
