using System.Text.Json;
using Wbskt.Workflow.Abstraction.Models.Expressions;
using Wbskt.Workflow.Abstraction.Enums;
using Xunit;

namespace Wbskt.Workflow.Engine.Host.Tests.Abstraction;

public class WorkflowExpressionTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    [Fact]
    public void BinaryExpression_roundtrips_json()
    {
        WorkflowExpression expr = new BinaryExpression(
            new MemberAccessExpression("$trigger", "temperature"),
            BinaryOperator.GreaterThan,
            new LiteralExpression(35));

        var json = JsonSerializer.Serialize(expr, Options);
        var back = JsonSerializer.Deserialize<WorkflowExpression>(json, Options);

        var bin = Assert.IsType<BinaryExpression>(back);
        var left = Assert.IsType<MemberAccessExpression>(bin.Left);
        Assert.Equal("$trigger", left.Object);
        Assert.Equal("temperature", left.Member);
        Assert.Equal(BinaryOperator.GreaterThan, bin.Operator);
        var right = Assert.IsType<LiteralExpression>(bin.Right);
        Assert.Equal(35, ((JsonElement)right.Value!).GetInt32());
    }

    [Fact]
    public void LiteralExpression_roundtrips_string()
    {
        WorkflowExpression expr = new LiteralExpression("hello");
        var json = JsonSerializer.Serialize(expr, Options);
        var back = JsonSerializer.Deserialize<WorkflowExpression>(json, Options);
        var lit = Assert.IsType<LiteralExpression>(back);
        Assert.Equal("hello", ((JsonElement)lit.Value!).GetString());
    }
}
