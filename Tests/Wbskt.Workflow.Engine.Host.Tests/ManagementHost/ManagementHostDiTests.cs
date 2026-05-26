using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wbskt.Management.Host.Services;
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
        services.AddWorkflowEngine(configuration, includeHostedServices: false);
        services.AddScoped<IWorkflowDefinitionService, WorkflowDefinitionService>();
        services.AddScoped<IWorkflowRunQueryService, WorkflowRunQueryService>();
        services.AddSingleton<WorkflowValidator>();

        using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using IServiceScope scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IWorkflowRunQueryService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IInboundHub>());
        Assert.Empty(provider.GetServices<IHostedService>());
    }
}
