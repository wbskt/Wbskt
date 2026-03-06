using System.Text.Json.Serialization;

namespace Wbskt.Workflow.Abstraction.Models.Expressions;

public sealed class MemberAccessExpression : WorkflowExpression
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty; // e.g., "$trigger.payload.temp"
}
