using System.Text.Json.Serialization;
using Wbskt.Workflow.Abstraction.Templates;

namespace Wbskt.Workflow.Abstraction.Models.Nodes.Actions;

public sealed record DiscordConfig
{
    /// <summary>The Discord webhook integration to post through.</summary>
    [JsonPropertyName("integrationRef")]
    public Guid? IntegrationRef { get; init; }

    /// <summary>The message text. Optional when the step has at least one embed.</summary>
    [Templated]
    [JsonPropertyName("message")]
    public string? Message { get; init; }

    /// <summary>Optional: the name the message is posted under, in place of the webhook's own.</summary>
    [Templated(TemplateText.Line)]
    [JsonPropertyName("username")]
    public string? Username { get; init; }

    /// <summary>Optional: the picture the message is posted with, in place of the webhook's own.</summary>
    [Templated(TemplateText.Line)]
    [JsonPropertyName("avatarUrl")]
    public string? AvatarUrl { get; init; }

    /// <summary>Optional: a thread in the webhook's channel to post into. Discord unarchives it.</summary>
    [Templated(TemplateText.Line)]
    [JsonPropertyName("threadId")]
    public string? ThreadId { get; init; }

    [Templated]
    [JsonPropertyName("embeds")]
    public IReadOnlyList<DiscordEmbed>? Embeds { get; init; }
}
