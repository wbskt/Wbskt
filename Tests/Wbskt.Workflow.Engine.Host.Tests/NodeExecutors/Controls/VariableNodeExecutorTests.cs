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
    public async Task ExecuteAsync_shared_scope_sets_shared_variable()
    {
        var provider = new RecordingSharedVariableProvider([1]);
        var evaluator = new MockExpressionEvaluator();
        var executor = new VariableNodeExecutor(provider, evaluator);
        NodeContext context = CreateContext(new VariableNode { NodeId = Guid.NewGuid(), Name = "set-shared", Ports = CreatePorts(), Config = new VariableConfig { Scope = VariableScope.Shared, Op = VariableOperation.Set, Var = "mode", Value = JsonSerializer.SerializeToElement("cool") } });

        NodeExecutionResult result = await executor.ExecuteAsync(context, CancellationToken.None);

        var continuation = Assert.IsType<NodeExecutionResult.Continue>(result);
        Assert.Equal("default", continuation.OutboundPort);
        Assert.Single(provider.CompareAndSetCalls);
        Assert.Equal("\"cool\"", provider.CompareAndSetCalls[0].NewValue);
    }

    [Fact]
    public async Task ExecuteAsync_shared_scope_retries_on_compare_and_set_conflict()
    {
        var provider = new RecordingSharedVariableProvider([0, 0, 1]);
        var evaluator = new MockExpressionEvaluator();
        var executor = new VariableNodeExecutor(provider, evaluator);
        NodeContext context = CreateContext(new VariableNode { NodeId = Guid.NewGuid(), Name = "set-shared", Ports = CreatePorts(), Config = new VariableConfig { Scope = VariableScope.Shared, Op = VariableOperation.Set, Var = "mode", Value = JsonSerializer.SerializeToElement("heat") } });

        NodeExecutionResult result = await executor.ExecuteAsync(context, CancellationToken.None);

        Assert.IsType<NodeExecutionResult.Continue>(result);
        Assert.Equal(3, provider.CompareAndSetCalls.Count);
        Assert.Equal(3, provider.GetCalls);
    }

    [Fact]
    public async Task ExecuteAsync_shared_scope_fails_after_three_conflicts()
    {
        var provider = new RecordingSharedVariableProvider([0, 0, 0]);
        var evaluator = new MockExpressionEvaluator();
        var executor = new VariableNodeExecutor(provider, evaluator);
        NodeContext context = CreateContext(new VariableNode { NodeId = Guid.NewGuid(), Name = "set-shared", Ports = CreatePorts(), Config = new VariableConfig { Scope = VariableScope.Shared, Op = VariableOperation.Set, Var = "mode", Value = JsonSerializer.SerializeToElement("heat") } });

        NodeExecutionResult result = await executor.ExecuteAsync(context, CancellationToken.None);

        var failure = Assert.IsType<NodeExecutionResult.Fail>(result);
        Assert.Equal("SHARED_VAR_CAS_FAILED", failure.ErrorCode);
        Assert.True(failure.Retryable);
    }

    private static NodeContext CreateContext(VariableNode node)
    {
        return new NodeContext
        {
            Branch = new BranchContext(42, 1001, 5, Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), 1, node.NodeId.ToString(), 1, new Dictionary<string, JsonElement>(), new Dictionary<string, JsonElement>(), "corr-1", DateTime.UtcNow),
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

    private sealed class RecordingSharedVariableProvider(IEnumerable<int>? compareAndSetResults = null) : ISharedVariableProvider
    {
        private readonly Queue<int> _compareAndSetResults = new(compareAndSetResults ?? [1]);

        public int GetCalls { get; private set; }

        public List<(Guid WorkflowRefId, string VarName, string Expected, string NewValue)> CompareAndSetCalls { get; } = [];

        public Task<SharedVariableRow> GetByWorkflowRefIdNameAsync(Guid workflowRefId, string varName, CancellationToken ct)
        {
            GetCalls++;
            return Task.FromResult(new SharedVariableRow
            {
                Id = 5,
                WorkflowRefId = workflowRefId,
                VarName = varName,
                VarType = "Json",
                ValueJson = $"\"current-{GetCalls}\"",
                UpdatedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            });
        }

        public Task<SharedVariableRow> InitializeAsync(Guid workflowRefId, string varName, string varType, string valueJson, CancellationToken ct)
        {
            throw new NotSupportedException();
        }

        public Task<SharedVariableRow> SetAsync(Guid workflowRefId, string varName, string valueJson, CancellationToken ct)
        {
            throw new NotSupportedException();
        }

        public Task<string> IncrementAsync(Guid workflowRefId, string varName, long delta, CancellationToken ct)
        {
            throw new NotSupportedException();
        }

        public Task<string> DecrementAsync(Guid workflowRefId, string varName, long delta, CancellationToken ct)
        {
            throw new NotSupportedException();
        }

        public Task<int> CompareAndSetAsync(Guid workflowRefId, string varName, string expected, string newValue, CancellationToken ct)
        {
            CompareAndSetCalls.Add((workflowRefId, varName, expected, newValue));
            return Task.FromResult(_compareAndSetResults.Dequeue());
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
