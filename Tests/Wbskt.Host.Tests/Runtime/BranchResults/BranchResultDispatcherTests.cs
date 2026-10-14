using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Extensions;
using Wbskt.Workflow.Runtime.BranchResults;
using static Wbskt.Host.Tests.Runtime.BranchResults.BranchResultTestData;

namespace Wbskt.Host.Tests.Runtime.BranchResults;

public sealed class BranchResultDispatcherTests
{
    private static readonly Guid NodeA = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task Hands_a_result_to_the_handler_for_its_type()
    {
        var terminal = new StubHandler<NodeExecutionResult.Terminal>();
        var fail = new StubHandler<NodeExecutionResult.Fail>();
        var dispatcher = new BranchResultDispatcher([terminal, fail]);
        var result = new NodeExecutionResult.Fail("BOOM", "node blew up", false, null);

        BranchResultOutcome outcome = await dispatcher.DispatchAsync(result, Context(Node(NodeA), Definition([Node(NodeA)], []), Branch(NodeA)), CancellationToken.None);

        Assert.True(outcome.IsStopped);
        Assert.Same(result, Assert.Single(fail.Handled));
        Assert.Empty(terminal.Handled);
    }

    [Fact]
    public async Task A_result_type_with_no_handler_throws_naming_the_type()
    {
        var dispatcher = new BranchResultDispatcher([new StubHandler<NodeExecutionResult.Terminal>()]);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            dispatcher.DispatchAsync(new UnhandledResult(), Context(Node(NodeA), Definition([Node(NodeA)], []), Branch(NodeA)), CancellationToken.None));

        Assert.Contains(nameof(UnhandledResult), exception.Message);
    }

    [Fact]
    public void Two_handlers_for_one_type_are_refused()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new BranchResultDispatcher([new StubHandler<NodeExecutionResult.Fork>(), new StubHandler<NodeExecutionResult.Fork>()]));

        Assert.Contains(nameof(NodeExecutionResult.Fork), exception.Message);
    }

    /// <summary>
    /// A node can return any <see cref="NodeExecutionResult"/> subtype, so the container must hold a
    /// handler for each; a new result type without one would otherwise only fail when a node first
    /// returns it.
    /// </summary>
    [Fact]
    public void Every_result_type_has_a_registered_handler()
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

        HashSet<Type> handled = scope.ServiceProvider
            .GetServices<IBranchResultHandler>()
            .Select(handler => handler.ResultType)
            .ToHashSet();
        Type[] resultTypes = typeof(NodeExecutionResult)
            .GetNestedTypes()
            .Where(type => type.IsSubclassOf(typeof(NodeExecutionResult)))
            .ToArray();

        Assert.NotEmpty(resultTypes);
        Type[] missing = resultTypes.Where(type => !handled.Contains(type)).ToArray();
        Assert.True(missing.Length == 0, $"No branch result handler registered for: {string.Join(", ", missing.Select(type => type.Name))}");
        Assert.IsType<BranchResultDispatcher>(scope.ServiceProvider.GetRequiredService<IBranchResultDispatcher>());
    }

    private sealed record UnhandledResult : NodeExecutionResult;

    private sealed class StubHandler<TResult> : BranchResultHandler<TResult>
        where TResult : NodeExecutionResult
    {
        public List<TResult> Handled { get; } = [];

        public override Task<BranchResultOutcome> HandleAsync(TResult result, BranchResultContext context, CancellationToken ct)
        {
            Handled.Add(result);
            return Task.FromResult(BranchResultOutcome.Stopped);
        }
    }
}
