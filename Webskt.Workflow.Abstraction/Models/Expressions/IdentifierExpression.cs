using System.Text.Json.Serialization;

namespace Webskt.Workflow.Abstraction.Models.Expressions;

public class IdentifierExpression : WorkflowExpression
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}