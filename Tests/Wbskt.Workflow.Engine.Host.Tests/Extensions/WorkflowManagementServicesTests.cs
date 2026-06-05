using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Extensions;

namespace Wbskt.Workflow.Engine.Host.Tests.Extensions;

public sealed class WorkflowManagementServicesTests
{
    private static IServiceCollection BuildServices()
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
        services.AddWorkflowManagementServices(configuration);
        return services;
    }

    [Fact]
    public void AddWorkflowManagementServices_resolves_IWorkflowDefinitionProvider()
    {
        using ServiceProvider provider = BuildServices().BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using IServiceScope scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionProvider>());
    }

    [Fact]
    public void AddWorkflowManagementServices_resolves_IRunProvider()
    {
        using ServiceProvider provider = BuildServices().BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using IServiceScope scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IRunProvider>());
    }

    [Fact]
    public void AddWorkflowManagementServices_resolves_IBranchProvider()
    {
        using ServiceProvider provider = BuildServices().BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using IServiceScope scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IBranchProvider>());
    }

    [Fact]
    public void AddWorkflowManagementServices_resolves_IHistoryEventProvider()
    {
        using ServiceProvider provider = BuildServices().BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using IServiceScope scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IHistoryEventProvider>());
    }

    [Fact]
    public void AddWorkflowManagementServices_resolves_IWorkflowDefinitionCache()
    {
        using ServiceProvider provider = BuildServices().BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using IServiceScope scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionCache>());
    }

    [Fact]
    public void AddWorkflowManagementServices_resolves_ITriggerRegistrationService()
    {
        using ServiceProvider provider = BuildServices().BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using IServiceScope scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ITriggerRegistrationService>());
    }

    [Fact]
    public void AddWorkflowManagementServices_resolves_IRunCancellationService()
    {
        using ServiceProvider provider = BuildServices().BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using IServiceScope scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IRunCancellationService>());
    }

    [Fact]
    public void AddWorkflowManagementServices_does_not_register_IInboundHub()
    {
        IServiceCollection services = BuildServices();

        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IInboundHub));
    }

    [Fact]
    public void AddWorkflowManagementServices_does_not_register_IRunDispatcher()
    {
        IServiceCollection services = BuildServices();

        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IRunDispatcher));
    }

    [Fact]
    public void AddWorkflowManagementServices_does_not_register_IBranchLoop()
    {
        IServiceCollection services = BuildServices();

        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IBranchLoop));
    }

    [Fact]
    public void AddWorkflowManagementServices_does_not_register_INodeExecutor()
    {
        IServiceCollection services = BuildServices();

        Assert.DoesNotContain(services, d => d.ServiceType == typeof(INodeExecutor));
    }
}
