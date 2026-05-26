using System.Text.Json.Serialization;

namespace Wbskt.Workflow.Abstraction.Models.Expressions;

public sealed record MemberAccessExpression(
    [property: JsonPropertyName("object")] string Object,
    [property: JsonPropertyName("member")] string Member
) : WorkflowExpression;
