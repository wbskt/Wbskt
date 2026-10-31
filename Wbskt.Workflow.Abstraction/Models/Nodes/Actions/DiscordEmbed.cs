using System.Text.Json.Serialization;
using Wbskt.Workflow.Abstraction.Templates;

namespace Wbskt.Workflow.Abstraction.Models.Nodes.Actions;

/// <summary>A rich card under a Discord message. Discord needs at least one visible part.</summary>
public sealed record DiscordEmbed
{
    [Templated]
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    /// <summary>Where the title links to.</summary>
    [Templated(TemplateText.Line)]
    [JsonPropertyName("url")]
    public string? Url { get; init; }

    [Templated]
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>The colour of the embed's edge, as <c>#RRGGBB</c>.</summary>
    [JsonPropertyName("color")]
    public string? Color { get; init; }

    [Templated]
    [JsonPropertyName("fields")]
    public IReadOnlyList<DiscordEmbedField>? Fields { get; init; }

    [Templated]
    [JsonPropertyName("footer")]
    public string? Footer { get; init; }

    [Templated(TemplateText.Line)]
    [JsonPropertyName("imageUrl")]
    public string? ImageUrl { get; init; }

    [Templated(TemplateText.Line)]
    [JsonPropertyName("thumbnailUrl")]
    public string? ThumbnailUrl { get; init; }

    /// <summary>When true, the embed shows the time the step sent it.</summary>
    [JsonPropertyName("timestamp")]
    public bool Timestamp { get; init; }
}
