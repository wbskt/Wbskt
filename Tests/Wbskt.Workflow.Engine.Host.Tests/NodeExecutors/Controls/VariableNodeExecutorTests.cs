using System.Text.Json;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Enums;
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
    public async Task ExecuteAsync_shared_counter_seeds_the_variable_when_it_does_not_exist()
    {
        var provider = new RecordingSharedVariableProvider { CounterThrowsNotFound = true };
        var executor = new VariableNodeExecutor(provider, new MockExpressionEvaluator());
        NodeContext context = CreateContext(new VariableNode { NodeId = Guid.NewGuid(), Name = "counter", Ports = CreatePorts(), Config = new VariableConfig { Scope = VariableScope.Shared, Op = VariableOperation.Increment, Var = "hits", Value = JsonSerializer.SerializeToElement(3) } });

        NodeExecutionResult result = await executor.ExecuteAsync(context, CancellationToken.None);

        Assert.IsType<NodeExecutionResult.Continue>(result);
        var initialize = Assert.Single(provider.InitializeCalls);
        Assert.Equal("Counter", initialize.VarType);
        Assert.Equal("3", initialize.ValueJson);
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
    public async Task ExecuteAsync_compare_and_set_is_rejected_with_an_explanation()
    {
        // The provider primitive exists, but VariableConfig carries no "expected" value to compare.
        var provider = new RecordingSharedVariableProvider();
        var executor = new VariableNodeExecutor(provider, new MockExpressionEvaluator());
        NodeContext context = CreateContext(new VariableNode { NodeId = Guid.NewGuid(), Name = "cas", Ports = CreatePorts(), Config = new VariableConfig { Scope = VariableScope.Shared, Op = VariableOperation.CompareAndSet, Var = "mode", Value = JsonSerializer.SerializeToElement("x") } });

        NodeExecutionResult result = await executor.ExecuteAsync(context, CancellationToken.None);

        var failure = Assert.IsType<NodeExecutionResult.Fail>(result);
        Assert.Equal("VARIABLE_OPERATION_NOT_SUPPORTED", failure.ErrorCode);
        Assert.Contains("expected", failure.Message);
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

    private static IReadOnlyCollection<PortDefinition> CreatePorts()
    {
        return [new PortDefinition { PortId = "default", Direction = PortDirection.Output, Label = "Default" }];
    }

    private sealed class RecordingSharedVariableProvider : ISharedVariableProvider
    {
        /// <summary>Makes SetAsync report the variable as never written (KeyNotFoundException).</summary>
        public bool SetThrowsNotFound { get; init; }

        /// <summary>Makes Increment/Decrement report no matching counter row.</summary>
        public bool CounterThrowsNotFound { get; init; }

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
            if (CounterThrowsNotFound)
            {
                throw new KeyNotFoundException(varName);
            }

            IncrementCalls.Add((varName, delta));
            return Task.FromResult(delta.ToString());
        }

        public Task<string> DecrementAsync(Guid workflowRefId, string varName, long delta, CancellationToken ct)
        {
            if (CounterThrowsNotFound)
            {
                throw new KeyNotFoundException(varName);
            }

            DecrementCalls.Add((varName, delta));
            return Task.FromResult((-delta).ToString());
        }

        public Task<int> CompareAndSetAsync(Guid workflowRefId, string varName, string expected, string newValue, CancellationToken ct)
        {
            CompareAndSetCalls.Add((workflowRefId, varName, expected, newValue));
            return Task.FromResult(1);
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
