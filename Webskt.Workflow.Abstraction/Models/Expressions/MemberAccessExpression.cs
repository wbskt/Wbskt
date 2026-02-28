using System.Text.Json.Serialization;

namespace Webskt.Workflow.Abstraction.Models.Expressions;

public sealed class MemberAccessExpression : WorkflowExpression
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty; // e.g., "$trigger.payload.temp"
}
