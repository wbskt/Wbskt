using System.Text.Json;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Exceptions;
using Wbskt.Workflow.Abstraction.Models.Expressions;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Workflow.Engine.Host.Tests.Runtime;

public sealed class ExpressionEvaluatorTests
{
    private static readonly Guid WorkflowRefId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public async Task EvaluateAsync_literal_returns_value_directly()
    {
        IExpressionEvaluator evaluator = new ExpressionEvaluator(new SystemClock());

        JsonElement result = await evaluator.EvaluateAsync(new LiteralExpression(new { ok = true, count = 3 }), CreateBranchContext(), CancellationToken.None);

        Assert.True(result.GetProperty("ok").GetBoolean());
        Assert.Equal(3, result.GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task EvaluateAsync_branch_state_ref_resolves_simple_dot_path()
    {
        IExpressionEvaluator evaluator = new ExpressionEvaluator(new SystemClock());
        BranchContext context = CreateBranchContext(new Dictionary<string, JsonElement>
        {
            ["workflow"] = JsonSerializer.SerializeToElement(new { flags = new { enabled = true } })
        });

        JsonElement result = await evaluator.EvaluateAsync(new BranchStateRefExpression("workflow.flags.enabled"), context, CancellationToken.None);

        Assert.True(result.GetBoolean());
    }

    [Fact]
    public async Task EvaluateAsync_invalid_jsonpath_fails_cleanly()
    {
        IExpressionEvaluator evaluator = new ExpressionEvaluator(new SystemClock());

        var exception = await Assert.ThrowsAsync<ExpressionEvaluationException>(() =>
            evaluator.EvaluateAsync(
                new JsonPathExpression(new LiteralExpression(JsonSerializer.SerializeToElement(new int[0])), "$.length()"),
                CreateBranchContext(),
                CancellationToken.None));

        Assert.Equal("EXPRESSION_EVALUATION_ERROR", exception.ErrorCode);
    }

    // ------------------------------------------------------------ comparison

    [Theory]
    // numbers
    [InlineData(30, BinaryOperator.GreaterThan, 10, true)]
    [InlineData(10, BinaryOperator.GreaterThan, 30, false)]
    [InlineData(10, BinaryOperator.GreaterThanOrEqual, 10, true)]
    [InlineData(9, BinaryOperator.LessThan, 10, true)]
    [InlineData(10, BinaryOperator.LessThanOrEqual, 9, false)]
    [InlineData(5, BinaryOperator.Equal, 5, true)]
    [InlineData(5, BinaryOperator.NotEqual, 5, false)]
    public async Task EvaluateAsync_compares_numbers(int left, BinaryOperator op, int right, bool expected)
    {
        IExpressionEvaluator evaluator = new ExpressionEvaluator(new SystemClock());

        JsonElement result = await evaluator.EvaluateAsync(
            new BinaryExpression(new LiteralExpression(left), op, new LiteralExpression(right)),
            CreateBranchContext(),
            CancellationToken.None);

        Assert.Equal(expected, result.GetBoolean());
    }

    [Fact]
    public async Task EvaluateAsync_compares_a_trigger_field_against_a_constant()
    {
        // The scenario the Logic node exists for: "if temperature > 30".
        IExpressionEvaluator evaluator = new ExpressionEvaluator(new SystemClock());
        BranchContext context = CreateBranchContext(new Dictionary<string, JsonElement>
        {
            ["reading"] = JsonSerializer.SerializeToElement(new { temperature = 34.5 })
        });

        JsonElement result = await evaluator.EvaluateAsync(
            new BinaryExpression(
                new BranchStateRefExpression("reading.temperature"),
                BinaryOperator.GreaterThan,
                new LiteralExpression(30)),
            context,
            CancellationToken.None);

        Assert.True(result.GetBoolean());
    }

    [Fact]
    public async Task EvaluateAsync_compares_strings_ordinally()
    {
        IExpressionEvaluator evaluator = new ExpressionEvaluator(new SystemClock());

        JsonElement result = await evaluator.EvaluateAsync(
            new BinaryExpression(new LiteralExpression("apple"), BinaryOperator.LessThan, new LiteralExpression("banana")),
            CreateBranchContext(),
            CancellationToken.None);

        Assert.True(result.GetBoolean());
    }

    [Fact]
    public async Task EvaluateAsync_does_not_coerce_a_numeric_string_to_a_number()
    {
        // Documented rule: "5" != 5. Equality across kinds is simply false, not an error.
        IExpressionEvaluator evaluator = new ExpressionEvaluator(new SystemClock());

        JsonElement result = await evaluator.EvaluateAsync(
            new BinaryExpression(new LiteralExpression("5"), BinaryOperator.Equal, new LiteralExpression(5)),
            CreateBranchContext(),
            CancellationToken.None);

        Assert.False(result.GetBoolean());
    }

    [Fact]
    public async Task EvaluateAsync_ordering_across_different_kinds_is_an_evaluation_error()
    {
        // Ordering has no defensible answer across kinds, so it fails loudly rather than guessing.
        IExpressionEvaluator evaluator = new ExpressionEvaluator(new SystemClock());

        var exception = await Assert.ThrowsAsync<ExpressionEvaluationException>(() =>
            evaluator.EvaluateAsync(
                new BinaryExpression(new LiteralExpression("abc"), BinaryOperator.GreaterThan, new LiteralExpression(5)),
                CreateBranchContext(),
                CancellationToken.None));

        Assert.Equal("EXPRESSION_EVALUATION_ERROR", exception.ErrorCode);
        Assert.Contains("a string and a number", exception.Message);
    }

    [Fact]
    public async Task EvaluateAsync_compares_objects_structurally_ignoring_property_order()
    {
        IExpressionEvaluator evaluator = new ExpressionEvaluator(new SystemClock());
        BranchContext context = CreateBranchContext(new Dictionary<string, JsonElement>
        {
            ["a"] = JsonSerializer.SerializeToElement(new { x = 1, y = 2 }),
            ["b"] = JsonSerializer.SerializeToElement(new { y = 2, x = 1 })
        });

        JsonElement result = await evaluator.EvaluateAsync(
            new BinaryExpression(new BranchStateRefExpression("a"), BinaryOperator.Equal, new BranchStateRefExpression("b")),
            context,
            CancellationToken.None);

        Assert.True(result.GetBoolean());
    }

    [Fact]
    public async Task EvaluateAsync_treats_a_missing_path_as_null_and_equal_to_null()
    {
        IExpressionEvaluator evaluator = new ExpressionEvaluator(new SystemClock());

        JsonElement result = await evaluator.EvaluateAsync(
            new BinaryExpression(
                new BranchStateRefExpression("nothing.here"),
                BinaryOperator.Equal,
                new LiteralExpression(null)),
            CreateBranchContext(),
            CancellationToken.None);

        Assert.True(result.GetBoolean());
    }

    // ------------------------------------------------------- boolean algebra

    [Theory]
    [InlineData(true, BinaryOperator.And, true, true)]
    [InlineData(true, BinaryOperator.And, false, false)]
    [InlineData(false, BinaryOperator.Or, true, true)]
    [InlineData(false, BinaryOperator.Or, false, false)]
    public async Task EvaluateAsync_evaluates_and_or(bool left, BinaryOperator op, bool right, bool expected)
    {
        IExpressionEvaluator evaluator = new ExpressionEvaluator(new SystemClock());

        JsonElement result = await evaluator.EvaluateAsync(
            new BinaryExpression(new LiteralExpression(left), op, new LiteralExpression(right)),
            CreateBranchContext(),
            CancellationToken.None);

        Assert.Equal(expected, result.GetBoolean());
    }

    [Fact]
    public async Task EvaluateAsync_short_circuits_And_without_evaluating_the_right_operand()
    {
        // `isPresent && value > 10` must not blow up on the comparison when isPresent is false.
        IExpressionEvaluator evaluator = new ExpressionEvaluator(new SystemClock());

        JsonElement result = await evaluator.EvaluateAsync(
            new BinaryExpression(
                new LiteralExpression(false),
                BinaryOperator.And,
                // Would throw if evaluated: ordering a string against a number.
                new BinaryExpression(new LiteralExpression("abc"), BinaryOperator.GreaterThan, new LiteralExpression(5))),
            CreateBranchContext(),
            CancellationToken.None);

        Assert.False(result.GetBoolean());
    }

    [Fact]
    public async Task EvaluateAsync_short_circuits_Or_without_evaluating_the_right_operand()
    {
        IExpressionEvaluator evaluator = new ExpressionEvaluator(new SystemClock());

        JsonElement result = await evaluator.EvaluateAsync(
            new BinaryExpression(
                new LiteralExpression(true),
                BinaryOperator.Or,
                new BinaryExpression(new LiteralExpression("abc"), BinaryOperator.GreaterThan, new LiteralExpression(5))),
            CreateBranchContext(),
            CancellationToken.None);

        Assert.True(result.GetBoolean());
    }

    [Fact]
    public async Task EvaluateAsync_rejects_a_non_boolean_operand_to_And()
    {
        // No truthiness: a non-empty string is not "true".
        IExpressionEvaluator evaluator = new ExpressionEvaluator(new SystemClock());

        await Assert.ThrowsAsync<ExpressionEvaluationException>(() =>
            evaluator.EvaluateAsync(
                new BinaryExpression(new LiteralExpression("yes"), BinaryOperator.And, new LiteralExpression(true)),
                CreateBranchContext(),
                CancellationToken.None));
    }

    [Fact]
    public async Task EvaluateAsync_evaluates_unary_not_and_negate()
    {
        IExpressionEvaluator evaluator = new ExpressionEvaluator(new SystemClock());
        BranchContext context = CreateBranchContext();

        JsonElement not = await evaluator.EvaluateAsync(
            new UnaryExpression(UnaryOperator.Not, new LiteralExpression(false)), context, CancellationToken.None);
        JsonElement negate = await evaluator.EvaluateAsync(
            new UnaryExpression(UnaryOperator.Negate, new LiteralExpression(7)), context, CancellationToken.None);

        Assert.True(not.GetBoolean());
        Assert.Equal(-7, negate.GetDouble());
    }

    // ------------------------------------------------------------- functions

    [Fact]
    public async Task EvaluateAsync_evaluates_string_functions()
    {
        IExpressionEvaluator evaluator = new ExpressionEvaluator(new SystemClock());
        BranchContext context = CreateBranchContext();

        Assert.Equal("abc", (await Evaluate(FunctionName.ToLower, "ABC")).GetString());
        Assert.Equal("ABC", (await Evaluate(FunctionName.ToUpper, "abc")).GetString());
        Assert.Equal("42", (await Evaluate(FunctionName.ToString, 42)).GetString());
        Assert.True((await Evaluate(FunctionName.StartsWith, "hello world", "hello")).GetBoolean());
        Assert.True((await Evaluate(FunctionName.EndsWith, "hello world", "world")).GetBoolean());
        Assert.True((await Evaluate(FunctionName.Contains, "hello world", "lo wo")).GetBoolean());
        Assert.False((await Evaluate(FunctionName.Contains, "hello world", "nope")).GetBoolean());

        async Task<JsonElement> Evaluate(FunctionName name, params object?[] args)
        {
            return await evaluator.EvaluateAsync(
                new FunctionExpression(name, args.Select(a => (WorkflowExpression)new LiteralExpression(a)).ToList()),
                context,
                CancellationToken.None);
        }
    }

    [Fact]
    public async Task EvaluateAsync_Contains_tests_membership_for_arrays()
    {
        IExpressionEvaluator evaluator = new ExpressionEvaluator(new SystemClock());
        BranchContext context = CreateBranchContext(new Dictionary<string, JsonElement>
        {
            ["tags"] = JsonSerializer.SerializeToElement(new[] { "red", "green" })
        });

        JsonElement result = await evaluator.EvaluateAsync(
            new FunctionExpression(FunctionName.Contains, [new BranchStateRefExpression("tags"), new LiteralExpression("green")]),
            context,
            CancellationToken.None);

        Assert.True(result.GetBoolean());
    }

    [Theory]
    [InlineData(FunctionName.IsNull, null, true)]
    [InlineData(FunctionName.IsNull, "x", false)]
    [InlineData(FunctionName.IsEmpty, "", true)]
    [InlineData(FunctionName.IsEmpty, "x", false)]
    [InlineData(FunctionName.IsEmpty, null, true)]
    public async Task EvaluateAsync_evaluates_null_and_empty_predicates(FunctionName name, string? value, bool expected)
    {
        IExpressionEvaluator evaluator = new ExpressionEvaluator(new SystemClock());

        JsonElement result = await evaluator.EvaluateAsync(
            new FunctionExpression(name, [new LiteralExpression(value)]),
            CreateBranchContext(),
            CancellationToken.None);

        Assert.Equal(expected, result.GetBoolean());
    }

    [Fact]
    public async Task EvaluateAsync_IsEmpty_is_true_for_an_empty_array()
    {
        IExpressionEvaluator evaluator = new ExpressionEvaluator(new SystemClock());
        BranchContext context = CreateBranchContext(new Dictionary<string, JsonElement>
        {
            ["items"] = JsonSerializer.SerializeToElement(Array.Empty<string>())
        });

        JsonElement result = await evaluator.EvaluateAsync(
            new FunctionExpression(FunctionName.IsEmpty, [new BranchStateRefExpression("items")]),
            context,
            CancellationToken.None);

        Assert.True(result.GetBoolean());
    }

    [Fact]
    public async Task EvaluateAsync_UtcNow_uses_the_injected_clock()
    {
        var clock = new FixedClock(new DateTime(2026, 8, 6, 12, 30, 0, DateTimeKind.Utc));
        IExpressionEvaluator evaluator = new ExpressionEvaluator(clock);

        JsonElement result = await evaluator.EvaluateAsync(
            new FunctionExpression(FunctionName.UtcNow, []), CreateBranchContext(), CancellationToken.None);

        Assert.Equal(clock.UtcNow, DateTime.Parse(result.GetString()!, null, System.Globalization.DateTimeStyles.RoundtripKind));
    }

    [Fact]
    public async Task EvaluateAsync_rejects_a_function_called_with_the_wrong_argument_count()
    {
        IExpressionEvaluator evaluator = new ExpressionEvaluator(new SystemClock());

        var exception = await Assert.ThrowsAsync<ExpressionEvaluationException>(() =>
            evaluator.EvaluateAsync(
                new FunctionExpression(FunctionName.ToLower, []),
                CreateBranchContext(),
                CancellationToken.None));

        Assert.Contains("takes 1 argument(s), but 0 were supplied", exception.Message);
    }

    // ---------------------------------------------------------- member access

    [Fact]
    public async Task EvaluateAsync_member_access_reads_a_property_off_branch_state()
    {
        IExpressionEvaluator evaluator = new ExpressionEvaluator(new SystemClock());
        BranchContext context = CreateBranchContext(new Dictionary<string, JsonElement>
        {
            ["order"] = JsonSerializer.SerializeToElement(new { total = 99 })
        });

        JsonElement result = await evaluator.EvaluateAsync(
            new MemberAccessExpression("order", "total"), context, CancellationToken.None);

        Assert.Equal(99, result.GetInt32());
    }

    // ------------------------------------------------------- shared variables

    [Fact]
    public async Task EvaluateAsync_shared_variable_ref_reads_the_stored_value()
    {
        var provider = new StubSharedVariableProvider { ["counter"] = "42" };
        IExpressionEvaluator evaluator = new ExpressionEvaluator(new SystemClock(), provider);

        JsonElement result = await evaluator.EvaluateAsync(
            new SharedVariableRefExpression("counter"), CreateBranchContext(), CancellationToken.None);

        Assert.Equal(42, result.GetInt32());
        Assert.Equal(WorkflowRefId, provider.LastWorkflowRefId);
    }

    [Fact]
    public async Task EvaluateAsync_shared_variable_that_was_never_written_is_null()
    {
        // Same behaviour as a missing branch-state path, so a workflow can test isNull($shared.x)
        // before initialising it.
        var provider = new StubSharedVariableProvider();
        IExpressionEvaluator evaluator = new ExpressionEvaluator(new SystemClock(), provider);

        JsonElement result = await evaluator.EvaluateAsync(
            new SharedVariableRefExpression("never-set"), CreateBranchContext(), CancellationToken.None);

        Assert.Equal(JsonValueKind.Null, result.ValueKind);
    }

    [Fact]
    public async Task EvaluateAsync_compares_a_shared_counter_against_a_threshold()
    {
        // The design doc's flagship shared-counter scenario, which could not run at all before.
        var provider = new StubSharedVariableProvider { ["counter"] = "7" };
        IExpressionEvaluator evaluator = new ExpressionEvaluator(new SystemClock(), provider);

        JsonElement result = await evaluator.EvaluateAsync(
            new BinaryExpression(
                new SharedVariableRefExpression("counter"),
                BinaryOperator.GreaterThanOrEqual,
                new LiteralExpression(5)),
            CreateBranchContext(),
            CancellationToken.None);

        Assert.True(result.GetBoolean());
    }

    [Fact]
    public async Task EvaluateAsync_shared_variable_without_a_provider_fails_cleanly()
    {
        IExpressionEvaluator evaluator = new ExpressionEvaluator(new SystemClock());

        var exception = await Assert.ThrowsAsync<ExpressionEvaluationException>(() =>
            evaluator.EvaluateAsync(new SharedVariableRefExpression("counter"), CreateBranchContext(), CancellationToken.None));

        Assert.Equal("EXPRESSION_EVALUATION_ERROR", exception.ErrorCode);
    }

    // ------------------------------------------------------------- templates

    [Fact]
    public async Task EvaluateAsync_template_interpolates_state_and_shared_values()
    {
        var provider = new StubSharedVariableProvider { ["visits"] = "3" };
        IExpressionEvaluator evaluator = new ExpressionEvaluator(new SystemClock(), provider);
        BranchContext context = CreateBranchContext(new Dictionary<string, JsonElement>
        {
            ["user"] = JsonSerializer.SerializeToElement(new { name = "Sam" })
        });

        JsonElement result = await evaluator.EvaluateAsync(
            new TemplateExpression("Hello {{user.name}}, visit #{{$shared.visits}}!"),
            context,
            CancellationToken.None);

        Assert.Equal("Hello Sam, visit #3!", result.GetString());
    }

    [Fact]
    public async Task EvaluateAsync_template_renders_a_missing_reference_as_empty()
    {
        IExpressionEvaluator evaluator = new ExpressionEvaluator(new SystemClock());

        JsonElement result = await evaluator.EvaluateAsync(
            new TemplateExpression("[{{nothing.here}}]"), CreateBranchContext(), CancellationToken.None);

        Assert.Equal("[]", result.GetString());
    }

    [Fact]
    public async Task EvaluateAsync_template_without_placeholders_is_returned_verbatim()
    {
        IExpressionEvaluator evaluator = new ExpressionEvaluator(new SystemClock());

        JsonElement result = await evaluator.EvaluateAsync(
            new TemplateExpression("no placeholders here"), CreateBranchContext(), CancellationToken.None);

        Assert.Equal("no placeholders here", result.GetString());
    }

    [Fact]
    public async Task EvaluateAsync_template_with_an_unclosed_placeholder_fails_cleanly()
    {
        IExpressionEvaluator evaluator = new ExpressionEvaluator(new SystemClock());

        var exception = await Assert.ThrowsAsync<ExpressionEvaluationException>(() =>
            evaluator.EvaluateAsync(new TemplateExpression("Hello {{user.name"), CreateBranchContext(), CancellationToken.None));

        Assert.Contains("unclosed", exception.Message);
    }

    private sealed class FixedClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow { get; } = utcNow;
    }

    private sealed class StubSharedVariableProvider : ISharedVariableProvider
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

        public Guid? LastWorkflowRefId { get; private set; }

        public string this[string name] { set => _values[name] = value; }

        public Task<SharedVariableRow> GetByWorkflowRefIdNameAsync(Guid workflowRefId, string varName, CancellationToken ct)
        {
            LastWorkflowRefId = workflowRefId;

            if (!_values.TryGetValue(varName, out string? valueJson))
            {
                // Matches the real provider: an unknown variable is a KeyNotFoundException.
                throw new KeyNotFoundException(varName);
            }

            return Task.FromResult(new SharedVariableRow
            {
                Id = 1,
                WorkflowRefId = workflowRefId,
                VarName = varName,
                VarType = "Json",
                ValueJson = valueJson,
                UpdatedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            });
        }

        public Task<SharedVariableRow> InitializeAsync(Guid workflowRefId, string varName, string varType, string valueJson, CancellationToken ct) => throw new NotSupportedException();
        public Task<SharedVariableRow> SetAsync(Guid workflowRefId, string varName, string valueJson, CancellationToken ct) => throw new NotSupportedException();
        public Task<string> IncrementAsync(Guid workflowRefId, string varName, long delta, CancellationToken ct) => throw new NotSupportedException();
        public Task<string> DecrementAsync(Guid workflowRefId, string varName, long delta, CancellationToken ct) => throw new NotSupportedException();
        public Task<int> CompareAndSetAsync(Guid workflowRefId, string varName, string expected, string newValue, CancellationToken ct) => throw new NotSupportedException();
    }

    private static BranchContext CreateBranchContext(IReadOnlyDictionary<string, JsonElement>? localState = null)
    {
        return new BranchContext(
            42,
            1001,
            5,
            WorkflowRefId,
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
