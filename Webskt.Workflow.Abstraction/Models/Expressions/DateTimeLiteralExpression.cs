using System.Text.Json.Serialization;

namespace Webskt.Workflow.Abstraction.Models.Expressions;

public class DateTimeLiteralExpression : WorkflowExpression
{
    [JsonPropertyName("value")]
    public DateTime Value { get; set; }
}