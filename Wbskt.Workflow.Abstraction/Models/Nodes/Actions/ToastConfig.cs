using System.Text.Json.Serialization;
namespace Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
public sealed record ToastConfig(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("message")] string Message);
