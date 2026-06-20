using System.Text.Json.Serialization;
namespace Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
public sealed record EmailConfig
{
    [JsonPropertyName("to")]
    public required string To { get; init; }

    [JsonPropertyName("subject")]
    public required string Subject { get; init; }

    [JsonPropertyName("body")]
    public required string Body { get; init; }

}

