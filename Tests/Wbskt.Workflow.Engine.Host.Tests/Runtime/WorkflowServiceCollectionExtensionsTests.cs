using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Engine.Host.HostedServices;
using Wbskt.Workflow.Extensions;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Workflow.Engine.Host.Tests.Runtime;

public sealed class WorkflowServiceCollectionExtensionsTests
{
    [Fact]
    public void AddWorkflowRuntime_registers_phase5_services_with_expected_lifetimes()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Server=(localdb)\\MSSQLLocalDB;Database=Wbskt;Trusted_Connection=True;"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddWorkflowProviders();
        services.AddWorkflowRuntime();
        services.AddHostedService<BranchExecutionPump>();

        using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        // Act
        var dispatcher1 = provider.GetRequiredService<IRunDispatcher>();
        var dispatcher2 = provider.GetRequiredService<IRunDispatcher>();
        var registry1 = provider.GetRequiredService<INodeExecutorRegistry>();
        var registry2 = provider.GetRequiredService<INodeExecutorRegistry>();
        var cache1 = provider.GetRequiredService<IWorkflowDefinitionCache>();
        var cache2 = provider.GetRequiredService<IWorkflowDefinitionCache>();
        using IServiceScope scope1 = provider.CreateScope();
        using IServiceScope scope2 = provider.CreateScope();
        var loop1 = scope1.ServiceProvider.GetRequiredService<IBranchLoop>();
        var loop2 = scope2.ServiceProvider.GetRequiredService<IBranchLoop>();
        var hostedServices = provider.GetServices<IHostedService>().ToArray();

        // Assert
        Assert.IsType<ChannelRunDispatcher>(dispatcher1);
        Assert.Same(dispatcher1, dispatcher2);
        Assert.IsType<NodeExecutorRegistry>(registry1);
        Assert.Same(registry1, registry2);
        Assert.IsType<WorkflowDefinitionCache>(cache1);
        Assert.Same(cache1, cache2);
        Assert.IsType<BranchLoop>(loop1);
        Assert.NotSame(loop1, loop2);
        Assert.Contains(hostedServices, service => service is BranchExecutionPump);
    }
}
