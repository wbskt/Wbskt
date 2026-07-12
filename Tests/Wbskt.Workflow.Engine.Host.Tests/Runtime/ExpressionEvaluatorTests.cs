using System.Text.Json;
using Wbskt.Workflow.Abstraction.Models.Expressions;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Workflow.Engine.Host.Tests.Runtime;

public sealed class ExpressionEvaluatorTests
{
    [Fact]
    public async Task EvaluateAsync_literal_returns_value_directly()
    {
        IExpressionEvaluator evaluator = new ExpressionEvaluator();

        JsonElement result = await evaluator.EvaluateAsync(new LiteralExpression(new { ok = true, count = 3 }), CreateBranchContext(), CancellationToken.None);

        Assert.True(result.GetProperty("ok").GetBoolean());
        Assert.Equal(3, result.GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task EvaluateAsync_branch_state_ref_resolves_simple_dot_path()
    {
        IExpressionEvaluator evaluator = new ExpressionEvaluator();
        BranchContext context = CreateBranchContext(new Dictionary<string, JsonElement>
        {
            ["workflow"] = JsonSerializer.SerializeToElement(new { flags = new { enabled = true } })
        });

        JsonElement result = await evaluator.EvaluateAsync(new BranchStateRefExpression("workflow.flags.enabled"), context, CancellationToken.None);

        Assert.True(result.GetBoolean());
    }

    [Fact]
    public async Task EvaluateAsync_unsupported_expression_types_throw()
    {
        IExpressionEvaluator evaluator = new ExpressionEvaluator();

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => evaluator.EvaluateAsync(new JsonPathExpression(new LiteralExpression(JsonSerializer.SerializeToElement(new int[0])), "$.length()"), CreateBranchContext(), CancellationToken.None));
    }

    public static TheoryData<WorkflowExpression, string> NotImplementedExpressions()
    {
        return new TheoryData<WorkflowExpression, string>
        {
            { new SharedVariableRefExpression("counter"), "Expression type SharedVariableRefExpression not yet implemented" },
            { new TemplateExpression("Hello {{user.name}}"), "Expression type TemplateExpression not yet implemented" },
            { new JsonPathExpression(new LiteralExpression(new[] { 1, 2, 3 }), "$.length()"), "Expression type JsonPathExpression not yet implemented" }
        };
    }

    private static BranchContext CreateBranchContext(IReadOnlyDictionary<string, JsonElement>? localState = null)
    {
        return new BranchContext(
            42,
            1001,
            5,
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            1,
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb").ToString(),
            1,
            localState ?? new Dictionary<string, JsonElement>(),
            new Dictionary<string, JsonElement>(),
            "corr-1",
            new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc),
            9);
    }
}
