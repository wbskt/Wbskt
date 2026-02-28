using System.Text.Json.Serialization;
using Webskt.Workflow.Abstraction.Models.Nodes.Controls;

namespace Webskt.Workflow.Abstraction.Models.Expressions;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(IdentifierExpression), "FilterExIdentifier")]
[JsonDerivedType(typeof(StringLiteralExpression), "FilterExStringLiteral")]
[JsonDerivedType(typeof(DateTimeLiteralExpression), "FilterExDateTimeLiteral")]
[JsonDerivedType(typeof(NumberLiteralExpression), "FilterExNumberLiteral")]
[JsonDerivedType(typeof(BooleanLiteralExpression), "FilterExBoolLiteral")]
[JsonDerivedType(typeof(AndExpression), "FilterExBoolAnd")]
[JsonDerivedType(typeof(OrExpression), "FilterExBoolOr")]
[JsonDerivedType(typeof(EqualsExpression), "FilterExBoolEq")]
[JsonDerivedType(typeof(NotEqualsExpression), "FilterExBoolNeq")]
[JsonDerivedType(typeof(GreaterThanExpression), "FilterExBoolGt")]
[JsonDerivedType(typeof(GreaterThanOrEqualExpression), "FilterExBoolGte")]
[JsonDerivedType(typeof(LessThanExpression), "FilterExBoolLt")]
[JsonDerivedType(typeof(LessThanOrEqualExpression), "FilterExBoolLte")]
[JsonDerivedType(typeof(ContainsExpression), "FilterExBoolContains")]
public abstract class WorkflowExpression
{
}