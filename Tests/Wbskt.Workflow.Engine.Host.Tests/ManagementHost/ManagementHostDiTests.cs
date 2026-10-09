using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Wbskt.EventBus.Abstractions;
using Wbskt.Infrastructure.Events;
using Wbskt.Infrastructure.Security;
using Wbskt.Management.Host.Services.Clients;
using Wbskt.Management.Host.Services.Workflow;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Abstraction.Validation;
using Wbskt.Workflow.Extensions;

namespace Wbskt.Workflow.Engine.Host.Tests.ManagementHost;

public sealed class ManagementHostDiTests
{
    [Fact]
    public void Management_Host_registers_workflow_services()
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
        services.AddWorkflowManagementServices(configuration);
        services.AddSingleton<IIdentityService, IdentityService>();
        services.AddQueuedEventBus();
        services.AddSingleton(Mock.Of<IEventBus>());
        services.AddSingleton(Mock.Of<IWorkflowEngineClient>());
        services.AddScoped<IWorkflowEngineGateway, WorkflowEngineGateway>();
        services.AddScoped<IWorkflowQueryService, WorkflowQueryService>();
        services.AddScopedWithQueuedEvents<IWorkflowLifecycleService, WorkflowLifecycleService>();
        services.AddScoped<IWorkflowRunQueryService, WorkflowRunQueryService>();
        services.AddSingleton<WorkflowValidator>();

        using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using IServiceScope scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IWorkflowQueryService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IWorkflowLifecycleService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IWorkflowRunQueryService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IWorkflowEngineGateway>());
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IInboundHub));
    }
}
