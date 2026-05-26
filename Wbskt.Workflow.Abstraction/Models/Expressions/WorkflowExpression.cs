using System.Text.Json.Serialization;

namespace Wbskt.Workflow.Abstraction.Models.Expressions;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(LiteralExpression), "literal")]
[JsonDerivedType(typeof(BranchStateRefExpression), "branchStateRef")]
[JsonDerivedType(typeof(SharedVariableRefExpression), "sharedVariableRef")]
[JsonDerivedType(typeof(TemplateExpression), "template")]
[JsonDerivedType(typeof(JsonPathExpression), "jsonPath")]
[JsonDerivedType(typeof(MemberAccessExpression), "member")]
[JsonDerivedType(typeof(BinaryExpression), "binary")]
[JsonDerivedType(typeof(UnaryExpression), "unary")]
[JsonDerivedType(typeof(FunctionExpression), "function")]
public abstract record WorkflowExpression;
