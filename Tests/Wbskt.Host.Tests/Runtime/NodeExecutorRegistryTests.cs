using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Exceptions;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Extensions;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Host.Tests.Runtime;

public sealed class NodeExecutorRegistryTests
{
    [Fact]
    public void For_returns_registered_executor()
    {
        // Arrange
        var expected = new StubNodeExecutor(NodeKind.ControlLogic);
        var registry = new NodeExecutorRegistry([expected]);

        // Act
        var actual = registry.For(NodeKind.ControlLogic);

        // Assert
        Assert.Same(expected, actual);
    }

    /// <summary>
    /// The validator rejects kinds outside <see cref="NodeKind.Executable"/> at publish time, so that
    /// set must match what the engine can actually run. Without this the two drift and either a
    /// runnable node gets rejected, or an unrunnable one publishes and fails mid-run.
    /// </summary>
    [Fact]
    public void Every_executable_kind_has_a_registered_executor()
    {
        using ServiceProvider provider = BuildRuntime();
        using IServiceScope scope = provider.CreateScope();

        var registeredKinds = scope.ServiceProvider
            .GetServices<INodeExecutor>()
            .Select(executor => executor.Kind)
            .ToHashSet(StringComparer.Ordinal);

        string[] missing = NodeKind.Executable.Where(kind => !registeredKinds.Contains(kind)).ToArray();
        Assert.True(missing.Length == 0, $"No executor registered for executable kind(s): {string.Join(", ", missing)}");

        string[] unknown = registeredKinds.Where(kind => !NodeKind.All.Contains(kind)).ToArray();
        Assert.True(unknown.Length == 0, $"Executor registered for kind(s) missing from NodeKind.All: {string.Join(", ", unknown)}");
    }

    /// <summary>
    /// Pins every registered executor's replay answer, so a new executor - or a change to one - has to
    /// take a position here. A node wrongly called replay-safe repeats its effect after a crash (a
    /// shared Increment counted twice); one wrongly left at the default only writes idempotency rows
    /// it did not need. The node passed is a representative one of the executor's kind; the Variable
    /// node's per-configuration answers are pinned in RetryExecutorTests.
    /// </summary>
    [Fact]
    public void Every_registered_executor_has_a_pinned_replay_answer()
    {
        var expected = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            [NodeKind.TriggerClient] = true,
            [NodeKind.TriggerClientPresence] = true,
            [NodeKind.TriggerSchedule] = true,
            [NodeKind.TriggerWebhook] = true,
            [NodeKind.TriggerManual] = true,
            [NodeKind.ControlLogic] = true,
            [NodeKind.ControlForEach] = true,
            [NodeKind.ControlDelay] = true,
            [NodeKind.ControlWaitForHttp] = true,
            [NodeKind.ControlAwaitSignal] = true,
            [NodeKind.ControlEnd] = true,
            [NodeKind.ControlFailRun] = true,
            // A local Set: shared Increment, Decrement and CompareAndSet are not replay-safe.
            [NodeKind.ControlVariable] = true,
            // Each initialises a join aggregator under a fresh token, so a replay would open a second cohort.
            [NodeKind.ControlFork] = false,
            [NodeKind.ControlParallelForEach] = false,
            // A replayed winning contribution is answered with ShouldContinue = 0 and would end the cohort.
            [NodeKind.ControlJoin] = false,
            [NodeKind.ControlSubWorkflow] = false,
            [NodeKind.ActionClientMessage] = false,
            [NodeKind.ActionEmail] = false,
            [NodeKind.ActionWebhook] = false,
            [NodeKind.ActionTelegram] = false,
            [NodeKind.ActionToast] = false,
            [NodeKind.ActionDiscord] = false,
            [NodeKind.ActionNtfy] = false,
        };

        using ServiceProvider provider = BuildRuntime();
        using IServiceScope scope = provider.CreateScope();

        var actual = scope.ServiceProvider
            .GetServices<INodeExecutor>()
            .ToDictionary(executor => executor.Kind, executor => executor.IsReplaySafe(RepresentativeNode(executor.Kind)), StringComparer.Ordinal);

        Assert.Equal(expected.OrderBy(pair => pair.Key, StringComparer.Ordinal), actual.OrderBy(pair => pair.Key, StringComparer.Ordinal));
    }

    private static BaseNode RepresentativeNode(string kind)
    {
        return kind == NodeKind.ControlVariable
            ? new VariableNode { NodeId = Guid.NewGuid(), Name = "variable", Ports = [], Config = new VariableConfig { Scope = VariableScope.Local, Op = VariableOperation.Set, Var = "x" } }
            : new EndNode { NodeId = Guid.NewGuid(), Name = kind, Ports = [] };
    }

    private static ServiceProvider BuildRuntime()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Server=(localdb)\\MSSQLLocalDB;Database=Wbskt;Trusted_Connection=True;"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddHttpClient();
        services.AddWorkflowProviders();
        services.AddWorkflowRuntime();
        services.AddScoped<IDeviceCommandPublisher>(_ => Mock.Of<IDeviceCommandPublisher>());
        services.AddScoped<IToastPublisher>(_ => Mock.Of<IToastPublisher>());

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    [Fact]
    public void For_throws_when_unknown()
    {
        // Arrange
        var registry = new NodeExecutorRegistry([new StubNodeExecutor(NodeKind.ControlLogic)]);

        // Act
        var exception = Assert.Throws<NodeKindNotSupportedException>(() => registry.For(NodeKind.ActionEmail));

        // Assert - a dedicated type so BranchLoop can report NODE_KIND_NOT_SUPPORTED rather than
        // burying it in the generic EXECUTOR_CRASH bucket.
        Assert.Equal(NodeKind.ActionEmail, exception.Kind);
        Assert.Equal($"No executor is registered for node kind '{NodeKind.ActionEmail}'.", exception.Message);
    }

    [Fact]
    public void Constructor_throws_on_duplicate_NodeKind()
    {
        // Arrange
        var first = new StubNodeExecutor(NodeKind.ControlLogic);
        var second = new StubNodeExecutor(NodeKind.ControlLogic);

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() => new NodeExecutorRegistry([first, second]));

        // Assert
        Assert.Equal($"Duplicate executor for {NodeKind.ControlLogic}", exception.Message);
    }

    private sealed class StubNodeExecutor(string kind) : INodeExecutor
    {
        public string Kind { get; } = kind;

        public Task<NodeExecutionResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
        {
            return Task.FromResult<NodeExecutionResult>(new NodeExecutionResult.Terminal(BranchTerminalReason.Completed));
        }
    }
}
