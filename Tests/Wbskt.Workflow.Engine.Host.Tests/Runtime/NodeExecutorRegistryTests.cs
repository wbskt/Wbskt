using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Wbskt.Workflow.Abstraction.Exceptions;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Extensions;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Workflow.Engine.Host.Tests.Runtime;

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

        using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
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
