using System.Text.Json.Serialization;
using Wbskt.Workflow.Abstraction.Templates;

namespace Wbskt.Workflow.Abstraction.Models.Nodes.Actions;

public sealed record DiscordEmbedField
{
    [Templated]
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [Templated]
    [JsonPropertyName("value")]
    public string Value { get; init; } = string.Empty;

    /// <summary>When true, Discord may lay the field out beside its neighbours.</summary>
    [JsonPropertyName("inline")]
    public bool Inline { get; init; }
}
