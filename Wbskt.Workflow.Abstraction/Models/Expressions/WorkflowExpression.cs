using System.Text.Json.Serialization;

namespace Wbskt.Workflow.Abstraction.Models.Expressions;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(LiteralExpression), "literal")]
[JsonDerivedType(typeof(MemberAccessExpression), "member")]
[JsonDerivedType(typeof(BinaryExpression), "binary")]
[JsonDerivedType(typeof(UnaryExpression), "unary")]
[JsonDerivedType(typeof(FunctionExpression), "function")]
public abstract record WorkflowExpression;
