using System.Text.Json.Serialization;
namespace Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
public sealed record EmailConfig
{
    /// <summary>The SMTP integration to send through: its RefId, from the workspace's integrations.</summary>
    [JsonPropertyName("integrationRef")]
    public Guid? IntegrationRef { get; init; }

    [JsonPropertyName("to")]
    public required string To { get; init; }

    [JsonPropertyName("subject")]
    public required string Subject { get; init; }

    [JsonPropertyName("body")]
    public required string Body { get; init; }

}

