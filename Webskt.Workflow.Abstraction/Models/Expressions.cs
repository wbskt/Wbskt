using System.Text.Json.Serialization;

namespace Webskt.Workflow.Abstraction.Models;

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

public abstract class BinaryExpression : WorkflowExpression
{
    [JsonPropertyName("left")]
    public WorkflowExpression Left { get; set; } = default!;

    [JsonPropertyName("right")]
    public WorkflowExpression Right { get; set; } = default!;
}

public class IdentifierExpression : WorkflowExpression
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

public class StringLiteralExpression : WorkflowExpression
{
    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;
}

public class DateTimeLiteralExpression : WorkflowExpression
{
    [JsonPropertyName("value")]
    public DateTime Value { get; set; }
}

public class NumberLiteralExpression : WorkflowExpression
{
    [JsonPropertyName("value")]
    public double Value { get; set; }
}

public class BooleanLiteralExpression : WorkflowExpression
{
    [JsonPropertyName("value")]
    public bool Value { get; set; }
}

public class AndExpression : BinaryExpression { }
public class OrExpression : BinaryExpression { }
public class EqualsExpression : BinaryExpression { }
public class NotEqualsExpression : BinaryExpression { }
public class GreaterThanExpression : BinaryExpression { }
public class GreaterThanOrEqualExpression : BinaryExpression { }
public class LessThanExpression : BinaryExpression { }
public class LessThanOrEqualExpression : BinaryExpression { }
public class ContainsExpression : BinaryExpression { }
