using System.Text.Json.Serialization;
using Wbskt.Workflow.Abstraction.Templates;
namespace Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
public sealed record EmailConfig
{
    /// <summary>The SMTP integration to send through: its RefId, from the workspace's integrations.</summary>
    [JsonPropertyName("integrationRef")]
    public Guid? IntegrationRef { get; init; }

    [Templated(TemplateText.Line)]
    [JsonPropertyName("to")]
    public required string To { get; init; }

    [Templated(TemplateText.Line)]
    [JsonPropertyName("subject")]
    public required string Subject { get; init; }

    [Templated]
    [JsonPropertyName("body")]
    public required string Body { get; init; }

}

