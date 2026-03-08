using System.Text.Json;
using Microsoft.Extensions.Logging;
using Moq;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models.Expressions;
using Wbskt.Workflow.Engine.Host.Models;
using Wbskt.Workflow.Engine.Host.Models.TriggerContexts;
using Wbskt.Workflow.Engine.Host.Services.ExpressionEvaluators;
using ExecutionContext = Wbskt.Workflow.Engine.Host.Models.ExecutionContext;

namespace Wbskt.Workflow.Engine.Host.Tests.Services.ExpressionEvaluators;

public class WorkflowExpressionEvaluatorTests
{
    private readonly WorkflowExpressionEvaluator _evaluator;

    public WorkflowExpressionEvaluatorTests()
    {
        var loggerMock = new Mock<ILogger<WorkflowExpressionEvaluator>>();
        _evaluator = new WorkflowExpressionEvaluator(loggerMock.Object);
    }

    private static ExecutionContext CreateContext(BaseTriggerContext? trigger = null, Dictionary<string, object?>? state = null)
    {
        var instance = new WorkflowInstance
        {
            TriggerContext = trigger,
            State = state ?? new Dictionary<string, object?>()
        };
        return new ExecutionContext(instance, Guid.NewGuid());
    }

    [Theory]
    [InlineData("\"hello\"", "hello")]
    [InlineData("123.45", 123.45)]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("null", null)]
    public void Evaluate_LiteralExpression_ReturnsCorrectValue(string jsonValue, object? expectedValue)
    {
        // Arrange
        var jsonDocument = JsonDocument.Parse(jsonValue);
        var literal = new LiteralExpression { Value = jsonDocument.RootElement };
        var context = CreateContext();

        // Act
        var result = _evaluator.Evaluate(literal, context);

        // Assert
        Assert.Equal(expectedValue, result);
    }

    [Fact]
    public void Evaluate_LiteralExpression_WithNullValueProperty_ReturnsNull()
    {
        // Arrange
        var literal = new LiteralExpression { Value = null };
        var context = CreateContext();

        // Act
        var result = _evaluator.Evaluate(literal, context);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void Evaluate_MemberAccessExpression_TriggerPayload_ReturnsCorrectData()
    {
        // Arrange
        var payloadData = JsonDocument.Parse("{\"temperature\": 42.5}").RootElement;
        
        var triggerContext = new ClientPayloadTriggerContext
        {
            DeviceRefId = Guid.NewGuid(),
            Data = payloadData
        };

        var context = CreateContext(trigger: triggerContext);
        var accessExpression = new MemberAccessExpression { Path = "$trigger.temperature" };

        // Act
        var result = _evaluator.Evaluate(accessExpression, context);

        // Assert
        Assert.Equal(42.5, result);
    }

    [Fact]
    public void Evaluate_MemberAccessExpression_StateVariable_ReturnsCorrectData()
    {
        // Arrange
        var state = new Dictionary<string, object?>
        {
            { "userCount", 10 } // Boxed integer
        };
        
        var context = CreateContext(state: state);
        var accessExpression = new MemberAccessExpression { Path = "$state.userCount" };

        // Act
        var result = _evaluator.Evaluate(accessExpression, context);

        // Assert
        Assert.Equal(10, result);
    }

    [Theory]
    [InlineData(BinaryOperator.Add, "5", "10", 15.0)]
    [InlineData(BinaryOperator.Subtract, "20", "5", 15.0)]
    [InlineData(BinaryOperator.Multiply, "4", "5", 20.0)]
    [InlineData(BinaryOperator.Divide, "20", "4", 5.0)]
    [InlineData(BinaryOperator.Equal, "\"apple\"", "\"apple\"", true)]
    [InlineData(BinaryOperator.Equal, "\"apple\"", "\"orange\"", false)]
    [InlineData(BinaryOperator.GreaterThan, "10", "5", true)]
    [InlineData(BinaryOperator.And, "true", "true", true)]
    [InlineData(BinaryOperator.Or, "false", "true", true)]
    public void Evaluate_BinaryExpression_ReturnsCorrectResult(BinaryOperator op, string leftJson, string rightJson, object expected)
    {
        // Arrange
        var leftLit = new LiteralExpression { Value = JsonDocument.Parse(leftJson).RootElement };
        var rightLit = new LiteralExpression { Value = JsonDocument.Parse(rightJson).RootElement };

        var binExpression = new BinaryExpression
        {
            Left = leftLit,
            Right = rightLit,
            Operator = op
        };

        var context = CreateContext();

        // Act
        var result = _evaluator.Evaluate(binExpression, context);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(UnaryOperator.Not, "true", false)]
    [InlineData(UnaryOperator.Not, "false", true)]
    [InlineData(UnaryOperator.Negate, "10", -10.0)]
    [InlineData(UnaryOperator.Negate, "-5", 5.0)]
    [InlineData(UnaryOperator.IsNull, "null", true)]
    [InlineData(UnaryOperator.IsNull, "\"test\"", false)]
    [InlineData(UnaryOperator.IsEmpty, "\"\"", true)]
    [InlineData(UnaryOperator.IsEmpty, "\"hello\"", false)]
    [InlineData(UnaryOperator.IsEmpty, "null", true)]
    public void Evaluate_UnaryExpression_ReturnsCorrectResult(UnaryOperator op, string operandJson, object expected)
    {
        // Arrange
        var operandLit = new LiteralExpression { Value = JsonDocument.Parse(operandJson).RootElement };
        var unaryExpr = new UnaryExpression
        {
            Operator = op,
            Operand = operandLit
        };

        var context = CreateContext();

        // Act
        var result = _evaluator.Evaluate(unaryExpr, context);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(FunctionName.Upper, new[] { "\"hello\"" }, "HELLO")]
    [InlineData(FunctionName.Lower, new[] { "\"HELLO\"" }, "hello")]
    [InlineData(FunctionName.Trim, new[] { "\"  hello  \"" }, "hello")]
    [InlineData(FunctionName.Concat, new[] { "\"a\"", "\"b\"", "\"c\"" }, "abc")]
    [InlineData(FunctionName.Abs, new[] { "-10.5" }, 10.5)]
    [InlineData(FunctionName.Round, new[] { "10.4" }, 10.0)]
    [InlineData(FunctionName.Round, new[] { "10.6" }, 11.0)]
    [InlineData(FunctionName.Min, new[] { "5", "10", "2" }, 2.0)]
    [InlineData(FunctionName.Max, new[] { "5", "10", "2" }, 10.0)]
    [InlineData(FunctionName.Contains, new[] { "\"hello world\"", "\"world\"" }, true)]
    [InlineData(FunctionName.Contains, new[] { "\"hello world\"", "\"planet\"" }, false)]
    [InlineData(FunctionName.Coalesce, new[] { "null", "\"fallback\"" }, "fallback")]
    public void Evaluate_FunctionExpression_ReturnsCorrectResult(FunctionName fn, string[] argsJson, object expected)
    {
        // Arrange
        var args = new List<WorkflowExpression>();
        foreach (var argJson in argsJson)
        {
            args.Add(new LiteralExpression { Value = JsonDocument.Parse(argJson).RootElement });
        }

        var fnExpr = new FunctionExpression
        {
            Function = fn,
            Arguments = args
        };

        var context = CreateContext();

        // Act
        var result = _evaluator.Evaluate(fnExpr, context);

        // Assert
        Assert.Equal(expected, result);
    }
}
