using System.Text.Json.Serialization;
using Wbskt.Workflow.Abstraction.Templates;
namespace Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
public sealed record ToastConfig
{
    [Templated]
    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [Templated]
    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

}

