using System.Text.Json.Serialization;

namespace Webskt.Workflow.Abstraction.Models.Expressions;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(LiteralExpression), "literal")]
[JsonDerivedType(typeof(UnaryExpression), "unary")]
[JsonDerivedType(typeof(BinaryExpression), "binary")]
[JsonDerivedType(typeof(MemberAccessExpression), "access")]
[JsonDerivedType(typeof(FunctionExpression), "function")]
public abstract class WorkflowExpression
{
}
