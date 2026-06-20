using System.Text.Json.Serialization;
namespace Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
public sealed record ToastConfig
{
    [JsonPropertyName("title")]
    public required string Title { get; init; }

    [JsonPropertyName("message")]
    public required string Message { get; init; }

}

