using System.Text.Json.Serialization;
using Wbskt.Workflow.Abstraction.Templates;
namespace Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
public sealed record NtfyConfig
{
    /// <summary>The ntfy integration to publish through; it names the server and the topic.</summary>
    [JsonPropertyName("integrationRef")]
    public Guid? IntegrationRef { get; init; }

    [Templated]
    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    [Templated(TemplateText.Line)]
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    /// <summary>ntfy's priority, 1 (min) to 5 (max); the server's default (3) when absent.</summary>
    [JsonPropertyName("priority")]
    public int? Priority { get; init; }

    /// <summary>ntfy tags, which it also shows as emoji where a tag names one.</summary>
    [Templated(TemplateText.Line)]
    [JsonPropertyName("tags")]
    public IReadOnlyList<string>? Tags { get; init; }
}
