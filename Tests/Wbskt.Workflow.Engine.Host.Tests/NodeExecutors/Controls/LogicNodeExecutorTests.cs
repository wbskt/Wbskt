using System.Text.Json;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Expressions;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.NodeExecutors.Controls;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Workflow.Engine.Host.Tests.NodeExecutors.Controls;

public sealed class LogicNodeExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_true_condition_routes_to_true_port()
    {
        var executor = new LogicNodeExecutor(new ExpressionEvaluator(new SystemClock()));
        NodeContext context = CreateContext(new LogicGateNode { NodeId = Guid.NewGuid(), Name = "logic", Ports = CreatePorts(), Config = new LogicGateConfig { Condition = LogicConditionJsonConverter.FromLegacyString("decision") } }, new Dictionary<string, JsonElement>
        {
            ["decision"] = JsonSerializer.SerializeToElement(true)
        });

        NodeExecutionResult result = await executor.ExecuteAsync(context, CancellationToken.None);

        var continuation = Assert.IsType<NodeExecutionResult.Continue>(result);
        Assert.Equal("true", continuation.OutboundPort);
        Assert.Empty(continuation.LocalStatePatch);
    }

    [Fact]
    public async Task ExecuteAsync_false_condition_routes_to_false_port()
    {
        var executor = new LogicNodeExecutor(new ExpressionEvaluator(new SystemClock()));
        NodeContext context = CreateContext(new LogicGateNode { NodeId = Guid.NewGuid(), Name = "logic", Ports = CreatePorts(), Config = new LogicGateConfig { Condition = LogicConditionJsonConverter.FromLegacyString("decision") } }, new Dictionary<string, JsonElement>
        {
            ["decision"] = JsonSerializer.SerializeToElement(false)
        });

        NodeExecutionResult result = await executor.ExecuteAsync(context, CancellationToken.None);

        var continuation = Assert.IsType<NodeExecutionResult.Continue>(result);
        Assert.Equal("false", continuation.OutboundPort);
    }

    [Fact]
    public async Task ExecuteAsync_compares_a_value_against_a_constant()
    {
        // The headline case: before the condition became a structured expression this was impossible
        // to express - a Logic node could only read a pre-computed boolean out of branch state.
        var executor = new LogicNodeExecutor(new ExpressionEvaluator(new SystemClock()));
        NodeContext context = CreateContext(
            new LogicGateNode
            {
                NodeId = Guid.NewGuid(),
                Name = "logic",
                Ports = CreatePorts(),
                Config = new LogicGateConfig
                {
                    Condition = new BinaryExpression(
                        new BranchStateRefExpression("reading.temperature"),
                        BinaryOperator.GreaterThan,
                        new LiteralExpression(30))
                }
            },
            new Dictionary<string, JsonElement>
            {
                ["reading"] = JsonSerializer.SerializeToElement(new { temperature = 34.5 })
            });

        NodeExecutionResult result = await executor.ExecuteAsync(context, CancellationToken.None);

        var continuation = Assert.IsType<NodeExecutionResult.Continue>(result);
        Assert.Equal("true", continuation.OutboundPort);
    }

    [Fact]
    public async Task ExecuteAsync_routes_false_when_the_comparison_does_not_hold()
    {
        var executor = new LogicNodeExecutor(new ExpressionEvaluator(new SystemClock()));
        NodeContext context = CreateContext(
            new LogicGateNode
            {
                NodeId = Guid.NewGuid(),
                Name = "logic",
                Ports = CreatePorts(),
                Config = new LogicGateConfig
                {
                    Condition = new BinaryExpression(
                        new BranchStateRefExpression("reading.temperature"),
                        BinaryOperator.GreaterThan,
                        new LiteralExpression(30))
                }
            },
            new Dictionary<string, JsonElement>
            {
                ["reading"] = JsonSerializer.SerializeToElement(new { temperature = 12 })
            });

        NodeExecutionResult result = await executor.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal("false", Assert.IsType<NodeExecutionResult.Continue>(result).OutboundPort);
    }

    [Fact]
    public async Task ExecuteAsync_non_bool_condition_returns_fail()
    {
        var executor = new LogicNodeExecutor(new ExpressionEvaluator(new SystemClock()));
        NodeContext context = CreateContext(new LogicGateNode { NodeId = Guid.NewGuid(), Name = "logic", Ports = CreatePorts(), Config = new LogicGateConfig { Condition = LogicConditionJsonConverter.FromLegacyString("decision") } }, new Dictionary<string, JsonElement>
        {
            ["decision"] = JsonSerializer.SerializeToElement("yes")
        });

        NodeExecutionResult result = await executor.ExecuteAsync(context, CancellationToken.None);

        var failure = Assert.IsType<NodeExecutionResult.Fail>(result);
        Assert.Equal("LOGIC_CONDITION_NOT_BOOL", failure.ErrorCode);
        Assert.False(failure.Retryable);
    }

    private static NodeContext CreateContext(LogicGateNode node, IReadOnlyDictionary<string, JsonElement> localState)
    {
        return new NodeContext
        {
            Branch = new BranchContext(42, 1001, 5, Guid.NewGuid(), 1, node.NodeId.ToString(), 1, localState, new Dictionary<string, JsonElement>(), "corr-1", DateTime.UtcNow, 9),
            Node = node,
            Providers = new StubProviderComposite(),
            Tick = 1,
            ParentResults = null,
            CancellationToken = CancellationToken.None, IdempotencyKey = "test-key"
        };
    }

    private static IReadOnlyCollection<PortDefinition> CreatePorts()
    {
        return [
            new PortDefinition { PortId = "in", Direction = PortDirection.Input, Label = "In" },
            new PortDefinition { PortId = "true", Direction = PortDirection.Output, Label = "True" },
            new PortDefinition { PortId = "false", Direction = PortDirection.Output, Label = "False" }
        ];
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
}
