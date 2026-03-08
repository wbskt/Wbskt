using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wbskt.Workflow.Abstraction.Models.Expressions;

public sealed class LiteralExpression : WorkflowExpression
{
    [JsonPropertyName("value")]
    public JsonElement? Value { get; set; }
}
