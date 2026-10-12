using System.Text.Json.Serialization;
namespace Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
public sealed record DiscordConfig
{
    /// <summary>The Discord webhook integration to post through.</summary>
    [JsonPropertyName("integrationRef")]
    public Guid? IntegrationRef { get; init; }

    [JsonPropertyName("message")]
    public required string Message { get; init; }

    /// <summary>Optional: the name the message is posted under, in place of the webhook's own.</summary>
    [JsonPropertyName("username")]
    public string? Username { get; init; }
}
