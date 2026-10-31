using System.Text.Json.Serialization;
using Wbskt.Workflow.Abstraction.Templates;
namespace Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
public sealed record ToastConfig
{
    [Templated]
    [JsonPropertyName("title")]
    public required string Title { get; init; }

    [Templated]
    [JsonPropertyName("message")]
    public required string Message { get; init; }

}

