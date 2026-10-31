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
    public string To { get; init; } = string.Empty;

    [Templated(TemplateText.Line)]
    [JsonPropertyName("subject")]
    public string Subject { get; init; } = string.Empty;

    [Templated]
    [JsonPropertyName("body")]
    public string Body { get; init; } = string.Empty;

}

