using System.Text.Json;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Exceptions;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.NodeExecutors.Controls;

namespace Wbskt.Workflow.Engine.Host.Tests.NodeExecutors.Controls;

public sealed class VariableNodeExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_local_scope_patches_local_state()
    {
        var provider = new RecordingSharedVariableProvider();
        var evaluator = new MockExpressionEvaluator();
        var executor = new VariableNodeExecutor(provider, evaluator);
        NodeContext context = CreateContext(new VariableNode { NodeId = Guid.NewGuid(), Name = "set-local", Ports = CreatePorts(), Config = new VariableConfig { Scope = VariableScope.Local, Op = VariableOperation.Set, Var = "mode", Value = JsonSerializer.SerializeToElement("auto") } });

        NodeExecutionResult result = await executor.ExecuteAsync(context, CancellationToken.None);

        var continuation = Assert.IsType<NodeExecutionResult.Continue>(result);
        Assert.Equal("default", continuation.OutboundPort);
        Assert.Equal("auto", continuation.LocalStatePatch["mode"].GetString());
        Assert.Empty(provider.CompareAndSetCalls);
    }

    [Fact]
    public async Task ExecuteAsync_shared_scope_set_is_last_writer_wins()
    {
        // Set used to run a 3-attempt CAS loop that could fail the node outright under contention.
        // It is now a plain UPDATE - no compare-and-set, no retries, no spurious failure.
        var provider = new RecordingSharedVariableProvider();
        var executor = new VariableNodeExecutor(provider, new MockExpressionEvaluator());
        NodeContext context = CreateContext(new VariableNode { NodeId = Guid.NewGuid(), Name = "set-shared", Ports = CreatePorts(), Config = new VariableConfig { Scope = VariableScope.Shared, Op = VariableOperation.Set, Var = "mode", Value = JsonSerializer.SerializeToElement("cool") } });

        NodeExecutionResult result = await executor.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal("default", Assert.IsType<NodeExecutionResult.Continue>(result).OutboundPort);
        var set = Assert.Single(provider.SetCalls);
        Assert.Equal("mode", set.VarName);
        Assert.Equal("\"cool\"", set.ValueJson);
        Assert.Empty(provider.CompareAndSetCalls);
    }

    [Fact]
    public async Task ExecuteAsync_shared_scope_set_initialises_a_variable_that_does_not_exist()
    {
        // SharedVariable_Set only UPDATEs, so a never-written variable must be created.
        var provider = new RecordingSharedVariableProvider { SetThrowsNotFound = true };
        var executor = new VariableNodeExecutor(provider, new MockExpressionEvaluator());
        NodeContext context = CreateContext(new VariableNode { NodeId = Guid.NewGuid(), Name = "set-shared", Ports = CreatePorts(), Config = new VariableConfig { Scope = VariableScope.Shared, Op = VariableOperation.Set, Var = "mode", Value = JsonSerializer.SerializeToElement("cool") } });

        NodeExecutionResult result = await executor.ExecuteAsync(context, CancellationToken.None);

        Assert.IsType<NodeExecutionResult.Continue>(result);
        var initialize = Assert.Single(provider.InitializeCalls);
        Assert.Equal("Json", initialize.VarType);
        Assert.Equal("\"cool\"", initialize.ValueJson);
    }

    [Theory]
    [InlineData(VariableOperation.Increment, 1)]
    [InlineData(VariableOperation.Decrement, 1)]
    public async Task ExecuteAsync_shared_counter_uses_the_atomic_procedures(VariableOperation op, long expectedDelta)
    {
        var provider = new RecordingSharedVariableProvider();
        var executor = new VariableNodeExecutor(provider, new MockExpressionEvaluator());
        NodeContext context = CreateContext(new VariableNode { NodeId = Guid.NewGuid(), Name = "counter", Ports = CreatePorts(), Config = new VariableConfig { Scope = VariableScope.Shared, Op = op, Var = "hits", Value = null } });

        NodeExecutionResult result = await executor.ExecuteAsync(context, CancellationToken.None);

        Assert.IsType<NodeExecutionResult.Continue>(result);
        if (op == VariableOperation.Increment)
        {
            Assert.Equal(expectedDelta, Assert.Single(provider.IncrementCalls).Delta);
            Assert.Empty(provider.DecrementCalls);
        }
        else
        {
            Assert.Equal(expectedDelta, Assert.Single(provider.DecrementCalls).Delta);
            Assert.Empty(provider.IncrementCalls);
        }

        // Atomic in the UPDATE - there is no read-modify-write, so nothing is read first.
        Assert.Equal(0, provider.GetCalls);
    }

    [Fact]
    public async Task ExecuteAsync_shared_counter_honours_a_configured_step()
    {
        var provider = new RecordingSharedVariableProvider();
        var executor = new VariableNodeExecutor(provider, new MockExpressionEvaluator());
        NodeContext context = CreateContext(new VariableNode { NodeId = Guid.NewGuid(), Name = "counter", Ports = CreatePorts(), Config = new VariableConfig { Scope = VariableScope.Shared, Op = VariableOperation.Increment, Var = "hits", Value = JsonSerializer.SerializeToElement(5) } });

        await executor.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(5, Assert.Single(provider.IncrementCalls).Delta);
    }

    [Fact]
    public async Task ExecuteAsync_shared_counter_on_a_variable_that_is_not_a_counter_fails_the_node()
    {
        // The procedure itself creates a missing counter, so the only refusal left is a variable that
        // holds something else. It surfaces as a permanent node failure, not a SQL cast error.
        var provider = new RecordingSharedVariableProvider { CounterIsNotACounter = true };
        var executor = new VariableNodeExecutor(provider, new MockExpressionEvaluator());
        NodeContext context = CreateContext(new VariableNode { NodeId = Guid.NewGuid(), Name = "counter", Ports = CreatePorts(), Config = new VariableConfig { Scope = VariableScope.Shared, Op = VariableOperation.Increment, Var = "hits", Value = JsonSerializer.SerializeToElement(3) } });

        var ex = await Assert.ThrowsAsync<SharedVariableNotACounterException>(() => executor.ExecuteAsync(context, CancellationToken.None));

        Assert.Equal("VARIABLE_NOT_A_COUNTER", ex.ErrorCode);
        Assert.Empty(provider.InitializeCalls);
    }

    [Theory]
    [InlineData(VariableOperation.Increment, 7d)]
    [InlineData(VariableOperation.Decrement, 5d)]
    public async Task ExecuteAsync_local_counter_patches_arithmetic_into_branch_state(VariableOperation op, double expected)
    {
        var provider = new RecordingSharedVariableProvider();
        var executor = new VariableNodeExecutor(provider, new MockExpressionEvaluator());
        NodeContext context = CreateContext(
            new VariableNode { NodeId = Guid.NewGuid(), Name = "counter", Ports = CreatePorts(), Config = new VariableConfig { Scope = VariableScope.Local, Op = op, Var = "hits", Value = JsonSerializer.SerializeToElement(1) } },
            new Dictionary<string, JsonElement> { ["hits"] = JsonSerializer.SerializeToElement(6) });

        NodeExecutionResult result = await executor.ExecuteAsync(context, CancellationToken.None);

        var continuation = Assert.IsType<NodeExecutionResult.Continue>(result);
        Assert.Equal(expected, continuation.LocalStatePatch["hits"].GetDouble());
    }

    [Fact]
    public async Task ExecuteAsync_local_counter_treats_a_missing_value_as_zero()
    {
        var provider = new RecordingSharedVariableProvider();
        var executor = new VariableNodeExecutor(provider, new MockExpressionEvaluator());
        NodeContext context = CreateContext(new VariableNode { NodeId = Guid.NewGuid(), Name = "counter", Ports = CreatePorts(), Config = new VariableConfig { Scope = VariableScope.Local, Op = VariableOperation.Increment, Var = "fresh", Value = null } });

        NodeExecutionResult result = await executor.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(1d, Assert.IsType<NodeExecutionResult.Continue>(result).LocalStatePatch["fresh"].GetDouble());
    }

    [Fact]
    public async Task ExecuteAsync_counter_with_a_non_numeric_step_fails()
    {
        var provider = new RecordingSharedVariableProvider();
        var executor = new VariableNodeExecutor(provider, new MockExpressionEvaluator());
        NodeContext context = CreateContext(new VariableNode { NodeId = Guid.NewGuid(), Name = "counter", Ports = CreatePorts(), Config = new VariableConfig { Scope = VariableScope.Shared, Op = VariableOperation.Increment, Var = "hits", Value = JsonSerializer.SerializeToElement("lots") } });

        NodeExecutionResult result = await executor.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal("VARIABLE_STEP_NOT_NUMERIC", Assert.IsType<NodeExecutionResult.Fail>(result).ErrorCode);
    }

    [Fact]
    public async Task ExecuteAsync_compare_and_set_without_an_expected_value_is_rejected()
    {
        // There is nothing to compare against; writing unconditionally would silently turn this into
        // a Set, which is the exact race the operation exists to avoid.
        var provider = new RecordingSharedVariableProvider();
        var executor = new VariableNodeExecutor(provider, new MockExpressionEvaluator());
        NodeContext context = CreateContext(new VariableNode { NodeId = Guid.NewGuid(), Name = "cas", Ports = CreatePorts(), Config = new VariableConfig { Scope = VariableScope.Shared, Op = VariableOperation.CompareAndSet, Var = "mode", Value = JsonSerializer.SerializeToElement("x") } });

        NodeExecutionResult result = await executor.ExecuteAsync(context, CancellationToken.None);

        var failure = Assert.IsType<NodeExecutionResult.Fail>(result);
        Assert.Equal("VARIABLE_EXPECTED_MISSING", failure.ErrorCode);
        Assert.Empty(provider.CompareAndSetCalls);
    }

    [Fact]
    public async Task ExecuteAsync_shared_compare_and_set_passes_expected_and_new_value_to_the_provider()
    {
        var provider = new RecordingSharedVariableProvider();
        var executor = new VariableNodeExecutor(provider, new MockExpressionEvaluator());
        NodeContext context = CreateContext(new VariableNode { NodeId = Guid.NewGuid(), Name = "cas", Ports = CreatePorts(), Config = new VariableConfig { Scope = VariableScope.Shared, Op = VariableOperation.CompareAndSet, Var = "mode", Expected = JsonSerializer.SerializeToElement("idle"), Value = JsonSerializer.SerializeToElement("busy") } });

        NodeExecutionResult result = await executor.ExecuteAsync(context, CancellationToken.None);

        var call = Assert.Single(provider.CompareAndSetCalls);
        Assert.Equal("mode", call.VarName);
        Assert.Equal("\"idle\"", call.Expected);
        Assert.Equal("\"busy\"", call.NewValue);
        Assert.True(Assert.IsType<NodeExecutionResult.Continue>(result).LocalStatePatch["casSucceeded"].GetBoolean());
    }

    [Fact]
    public async Task ExecuteAsync_shared_compare_and_set_reports_a_lost_race_without_failing_the_node()
    {
        // Losing is a normal outcome - somebody else won, which is what the operation is for. Failing
        // the node would make the optimistic-concurrency retry pattern impossible to express.
        var provider = new RecordingSharedVariableProvider { CompareAndSetRowsAffected = 0 };
        var executor = new VariableNodeExecutor(provider, new MockExpressionEvaluator());
        NodeContext context = CreateContext(new VariableNode { NodeId = Guid.NewGuid(), Name = "cas", Ports = CreatePorts(), Config = new VariableConfig { Scope = VariableScope.Shared, Op = VariableOperation.CompareAndSet, Var = "mode", Expected = JsonSerializer.SerializeToElement("idle"), Value = JsonSerializer.SerializeToElement("busy") } });

        NodeExecutionResult result = await executor.ExecuteAsync(context, CancellationToken.None);

        var continuation = Assert.IsType<NodeExecutionResult.Continue>(result);
        Assert.Equal("default", continuation.OutboundPort);
        Assert.False(continuation.LocalStatePatch["casSucceeded"].GetBoolean());
    }

    [Fact]
    public async Task ExecuteAsync_shared_compare_and_set_sends_canonical_json()
    {
        var provider = new RecordingSharedVariableProvider();
        var executor = new VariableNodeExecutor(provider, new MockExpressionEvaluator());
        NodeContext context = CreateContext(new VariableNode { NodeId = Guid.NewGuid(), Name = "cas", Ports = CreatePorts(), Config = new VariableConfig { Scope = VariableScope.Shared, Op = VariableOperation.CompareAndSet, Var = "state", Expected = Parse("""{"b": 1.0, "a": [2.50]}"""), Value = Parse("""{"z": 1e2, "y": true}""") } });

        await executor.ExecuteAsync(context, CancellationToken.None);

        var call = Assert.Single(provider.CompareAndSetCalls);
        Assert.Equal("""{"a":[2.5],"b":1}""", call.Expected);
        Assert.Equal("""{"y":true,"z":100}""", call.NewValue);
    }

    [Fact]
    public async Task ExecuteAsync_shared_compare_and_set_retries_a_miss_against_the_value_as_it_was_stored_before()
    {
        // A value written before writes were canonical still holds its original spelling.
        var provider = new RecordingSharedVariableProvider { CompareAndSetRowsAffected = 0, CompareAndSetMatches = """{"b":1.0,"a":2}""" };
        var executor = new VariableNodeExecutor(provider, new MockExpressionEvaluator());
        NodeContext context = CreateContext(new VariableNode { NodeId = Guid.NewGuid(), Name = "cas", Ports = CreatePorts(), Config = new VariableConfig { Scope = VariableScope.Shared, Op = VariableOperation.CompareAndSet, Var = "state", Expected = Parse("""{"b": 1.0, "a": 2}"""), Value = Parse("3") } });

        var continuation = Assert.IsType<NodeExecutionResult.Continue>(await executor.ExecuteAsync(context, CancellationToken.None));

        Assert.Equal(["""{"a":2,"b":1}""", """{"b":1.0,"a":2}"""], provider.CompareAndSetCalls.Select(c => c.Expected));
        Assert.True(continuation.LocalStatePatch["casSucceeded"].GetBoolean());
    }

    [Fact]
    public async Task ExecuteAsync_shared_compare_and_set_does_not_retry_when_the_canonical_form_is_the_original()
    {
        var provider = new RecordingSharedVariableProvider { CompareAndSetRowsAffected = 0 };
        var executor = new VariableNodeExecutor(provider, new MockExpressionEvaluator());
        NodeContext context = CreateContext(new VariableNode { NodeId = Guid.NewGuid(), Name = "cas", Ports = CreatePorts(), Config = new VariableConfig { Scope = VariableScope.Shared, Op = VariableOperation.CompareAndSet, Var = "mode", Expected = JsonSerializer.SerializeToElement("idle"), Value = JsonSerializer.SerializeToElement("busy") } });

        await executor.ExecuteAsync(context, CancellationToken.None);

        Assert.Single(provider.CompareAndSetCalls);
    }

    [Fact]
    public async Task ExecuteAsync_shared_set_stores_canonical_json()
    {
        var provider = new RecordingSharedVariableProvider();
        var executor = new VariableNodeExecutor(provider, new MockExpressionEvaluator());
        NodeContext context = CreateContext(new VariableNode { NodeId = Guid.NewGuid(), Name = "set", Ports = CreatePorts(), Config = new VariableConfig { Scope = VariableScope.Shared, Op = VariableOperation.Set, Var = "state", Value = Parse("""{"b": 1.0, "a": 2}""") } });

        await executor.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal("""{"a":2,"b":1}""", Assert.Single(provider.SetCalls).ValueJson);
    }

    [Fact]
    public async Task ExecuteAsync_local_compare_and_set_matches_numbers_spelled_differently()
    {
        var provider = new RecordingSharedVariableProvider();
        var executor = new VariableNodeExecutor(provider, new MockExpressionEvaluator());
        NodeContext context = CreateContext(
            new VariableNode { NodeId = Guid.NewGuid(), Name = "cas", Ports = CreatePorts(), Config = new VariableConfig { Scope = VariableScope.Local, Op = VariableOperation.CompareAndSet, Var = "count", Expected = Parse("1"), Value = Parse("2") } },
            new Dictionary<string, JsonElement> { ["count"] = Parse("1.0") });

        var continuation = Assert.IsType<NodeExecutionResult.Continue>(await executor.ExecuteAsync(context, CancellationToken.None));

        Assert.True(continuation.LocalStatePatch["casSucceeded"].GetBoolean());
    }

    [Fact]
    public async Task ExecuteAsync_local_compare_and_set_writes_only_when_the_current_value_matches()
    {
        var provider = new RecordingSharedVariableProvider();
        var executor = new VariableNodeExecutor(provider, new MockExpressionEvaluator());
        NodeContext context = CreateContext(
            new VariableNode { NodeId = Guid.NewGuid(), Name = "cas", Ports = CreatePorts(), Config = new VariableConfig { Scope = VariableScope.Local, Op = VariableOperation.CompareAndSet, Var = "mode", Expected = JsonSerializer.SerializeToElement("idle"), Value = JsonSerializer.SerializeToElement("busy") } },
            new Dictionary<string, JsonElement> { ["mode"] = JsonSerializer.SerializeToElement("idle") });

        var continuation = Assert.IsType<NodeExecutionResult.Continue>(await executor.ExecuteAsync(context, CancellationToken.None));

        Assert.True(continuation.LocalStatePatch["casSucceeded"].GetBoolean());
        Assert.Equal("busy", continuation.LocalStatePatch["mode"].GetString());
        Assert.Empty(provider.CompareAndSetCalls);
    }

    [Fact]
    public async Task ExecuteAsync_local_compare_and_set_leaves_a_mismatched_value_alone()
    {
        var provider = new RecordingSharedVariableProvider();
        var executor = new VariableNodeExecutor(provider, new MockExpressionEvaluator());
        NodeContext context = CreateContext(
            new VariableNode { NodeId = Guid.NewGuid(), Name = "cas", Ports = CreatePorts(), Config = new VariableConfig { Scope = VariableScope.Local, Op = VariableOperation.CompareAndSet, Var = "mode", Expected = JsonSerializer.SerializeToElement("idle"), Value = JsonSerializer.SerializeToElement("busy") } },
            new Dictionary<string, JsonElement> { ["mode"] = JsonSerializer.SerializeToElement("already-busy") });

        var continuation = Assert.IsType<NodeExecutionResult.Continue>(await executor.ExecuteAsync(context, CancellationToken.None));

        Assert.False(continuation.LocalStatePatch["casSucceeded"].GetBoolean());
        Assert.DoesNotContain("mode", continuation.LocalStatePatch.Keys);
    }

    [Fact]
    public async Task A_shared_write_names_the_run_that_wrote_it()
    {
        var writes = new RecordingWrites();
        var executor = new VariableNodeExecutor(new RecordingSharedVariableProvider(), new MockExpressionEvaluator(), writes);
        NodeContext context = CreateContext(new VariableNode { NodeId = Guid.NewGuid(), Name = "set-shared", Ports = CreatePorts(), Config = new VariableConfig { Scope = VariableScope.Shared, Op = VariableOperation.Set, Var = "mode", Value = JsonSerializer.SerializeToElement("cool") } });

        await executor.ExecuteAsync(context, CancellationToken.None);

        var write = Assert.Single(writes.Writes);
        Assert.Equal(context.Branch.WorkflowDefinitionRefId, write.WorkflowRefId);
        Assert.Equal(context.Branch.RunRefId, write.RunRefId);
        Assert.Equal(context.Branch.WorkspaceId, write.WorkspaceId);
        Assert.Equal(("mode", "Set", "\"cool\""), (write.Name, write.Operation, write.ValueJson));
    }

    [Fact]
    public async Task A_counter_write_announces_the_value_it_reached()
    {
        var writes = new RecordingWrites();
        var executor = new VariableNodeExecutor(new RecordingSharedVariableProvider(), new MockExpressionEvaluator(), writes);
        NodeContext context = CreateContext(new VariableNode { NodeId = Guid.NewGuid(), Name = "counter", Ports = CreatePorts(), Config = new VariableConfig { Scope = VariableScope.Shared, Op = VariableOperation.Increment, Var = "hits", Value = JsonSerializer.SerializeToElement(3) } });

        await executor.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(("hits", "Increment", "3"), (Assert.Single(writes.Writes).Name, writes.Writes[0].Operation, writes.Writes[0].ValueJson));
    }

    [Fact]
    public async Task A_lost_compare_and_set_and_a_local_write_announce_nothing()
    {
        var writes = new RecordingWrites();
        var executor = new VariableNodeExecutor(new RecordingSharedVariableProvider { CompareAndSetRowsAffected = 0 }, new MockExpressionEvaluator(), writes);
        var cas = new VariableConfig { Scope = VariableScope.Shared, Op = VariableOperation.CompareAndSet, Var = "mode", Value = JsonSerializer.SerializeToElement("on"), Expected = JsonSerializer.SerializeToElement("off") };
        var local = new VariableConfig { Scope = VariableScope.Local, Op = VariableOperation.Set, Var = "mode", Value = JsonSerializer.SerializeToElement("on") };

        await executor.ExecuteAsync(CreateContext(new VariableNode { NodeId = Guid.NewGuid(), Name = "cas", Ports = CreatePorts(), Config = cas }), CancellationToken.None);
        await executor.ExecuteAsync(CreateContext(new VariableNode { NodeId = Guid.NewGuid(), Name = "local", Ports = CreatePorts(), Config = local }), CancellationToken.None);

        Assert.Empty(writes.Writes);
    }

    private sealed class RecordingWrites : IVariableWritePublisher
    {
        public List<VariableWrite> Writes { get; } = [];

        public Task PublishAsync(VariableWrite write, CancellationToken ct)
        {
            Writes.Add(write);
            return Task.CompletedTask;
        }
    }

    private static NodeContext CreateContext(VariableNode node, IReadOnlyDictionary<string, JsonElement>? localState = null)
    {
        return new NodeContext
        {
            Branch = new BranchContext(42, 1001, 5, Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), 1, node.NodeId.ToString(), 1, localState ?? new Dictionary<string, JsonElement>(), new Dictionary<string, JsonElement>(), "corr-1", DateTime.UtcNow, 9),
            Node = node,
            Providers = new StubProviderComposite(),
            Tick = 1,
            ParentResults = null,
            CancellationToken = CancellationToken.None, IdempotencyKey = "test-key"
        };
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static IReadOnlyCollection<PortDefinition> CreatePorts()
    {
        return [new PortDefinition { PortId = "default", Direction = PortDirection.Output, Label = "Default" }];
    }

    private sealed class RecordingSharedVariableProvider : ISharedVariableProvider
    {
        /// <summary>Makes SetAsync report the variable as never written (KeyNotFoundException).</summary>
        public bool SetThrowsNotFound { get; init; }

        /// <summary>Makes Increment/Decrement refuse the variable as not a counter.</summary>
        public bool CounterIsNotACounter { get; init; }

        public int GetCalls { get; private set; }

        public List<(Guid WorkflowRefId, string VarName, string Expected, string NewValue)> CompareAndSetCalls { get; } = [];

        public List<(string VarName, string ValueJson)> SetCalls { get; } = [];

        public List<(string VarName, string VarType, string ValueJson)> InitializeCalls { get; } = [];

        public List<(string VarName, long Delta)> IncrementCalls { get; } = [];

        public List<(string VarName, long Delta)> DecrementCalls { get; } = [];

        public Task<SharedVariableRow> GetByWorkflowRefIdNameAsync(Guid workflowRefId, string varName, CancellationToken ct)
        {
            GetCalls++;
            return Task.FromResult(Row(workflowRefId, varName, "Json", $"\"current-{GetCalls}\""));
        }

        public Task<SharedVariableRow> InitializeAsync(Guid workflowRefId, string varName, string varType, string valueJson, CancellationToken ct)
        {
            InitializeCalls.Add((varName, varType, valueJson));
            return Task.FromResult(Row(workflowRefId, varName, varType, valueJson));
        }

        public Task<SharedVariableRow> SetAsync(Guid workflowRefId, string varName, string valueJson, CancellationToken ct)
        {
            if (SetThrowsNotFound)
            {
                throw new KeyNotFoundException(varName);
            }

            SetCalls.Add((varName, valueJson));
            return Task.FromResult(Row(workflowRefId, varName, "Json", valueJson));
        }

        public Task<string> IncrementAsync(Guid workflowRefId, string varName, long delta, CancellationToken ct)
        {
            if (CounterIsNotACounter)
            {
                throw new SharedVariableNotACounterException(varName, new InvalidOperationException("50023"));
            }

            IncrementCalls.Add((varName, delta));
            return Task.FromResult(delta.ToString());
        }

        public Task<string> DecrementAsync(Guid workflowRefId, string varName, long delta, CancellationToken ct)
        {
            if (CounterIsNotACounter)
            {
                throw new SharedVariableNotACounterException(varName, new InvalidOperationException("50023"));
            }

            DecrementCalls.Add((varName, delta));
            return Task.FromResult((-delta).ToString());
        }

        /// <summary>Rows the compare-and-set procedure reports updating; 0 means the race was lost.</summary>
        public int CompareAndSetRowsAffected { get; init; } = 1;

        /// <summary>When set, a compare against exactly this text succeeds whatever <see cref="CompareAndSetRowsAffected"/> says.</summary>
        public string? CompareAndSetMatches { get; init; }

        public Task<int> CompareAndSetAsync(Guid workflowRefId, string varName, string expected, string newValue, CancellationToken ct)
        {
            CompareAndSetCalls.Add((workflowRefId, varName, expected, newValue));
            return Task.FromResult(expected == CompareAndSetMatches ? 1 : CompareAndSetRowsAffected);
        }

        private static SharedVariableRow Row(Guid workflowRefId, string varName, string varType, string valueJson)
        {
            return new SharedVariableRow
            {
                Id = 5,
                WorkflowRefId = workflowRefId,
                VarName = varName,
                VarType = varType,
                ValueJson = valueJson,
                UpdatedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };
        }
    }

    private sealed class StubProviderComposite : IProviderComposite
    {
        public IWorkflowDefinitionProvider WorkflowDefinition => throw new NotSupportedException();
        public ITriggerRegistrationProvider TriggerRegistration => throw new NotSupportedException();
        public IBookmarkProvider Bookmark => throw new NotSupportedException();
        public ISharedVariableProvider SharedVariable => throw new NotSupportedException();
        public IIdempotencyKeyProvider IdempotencyKey => throw new NotSupportedException();
        public IPendingTriggerEventProvider PendingTriggerEvent => throw new NotSupportedException();
        public IScheduledFireProvider ScheduledFire => throw new NotSupportedException();
    }

    private sealed class MockExpressionEvaluator : IExpressionEvaluator
    {
        public Task<JsonElement> EvaluateAsync(Wbskt.Workflow.Abstraction.Models.Expressions.WorkflowExpression expr, BranchContext context, CancellationToken ct)
        {
            return Task.FromResult(JsonSerializer.SerializeToElement("evaluated"));
        }
    }
}
