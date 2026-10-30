using System.Text.Json.Serialization;

namespace Wbskt.Workflow.Abstraction.Models.Nodes.Actions;

/// <summary>A rich card under a Discord message. Discord needs at least one visible part.</summary>
public sealed record DiscordEmbed
{
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    /// <summary>Where the title links to.</summary>
    [JsonPropertyName("url")]
    public string? Url { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>The colour of the embed's edge, as <c>#RRGGBB</c>.</summary>
    [JsonPropertyName("color")]
    public string? Color { get; init; }

    [JsonPropertyName("fields")]
    public IReadOnlyList<DiscordEmbedField>? Fields { get; init; }

    [JsonPropertyName("footer")]
    public string? Footer { get; init; }

    [JsonPropertyName("imageUrl")]
    public string? ImageUrl { get; init; }

    [JsonPropertyName("thumbnailUrl")]
    public string? ThumbnailUrl { get; init; }

    /// <summary>When true, the embed shows the time the step sent it.</summary>
    [JsonPropertyName("timestamp")]
    public bool Timestamp { get; init; }
}
