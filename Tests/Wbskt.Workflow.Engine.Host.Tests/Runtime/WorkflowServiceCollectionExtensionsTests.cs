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
    public void AddWorkflowRuntime_registers_phase8_services_with_expected_lifetimes()
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
        services.AddHostedService<BookmarkScheduler>();

        using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        // Act
        var dispatcher1 = provider.GetRequiredService<IRunDispatcher>();
        var dispatcher2 = provider.GetRequiredService<IRunDispatcher>();
        var cache1 = provider.GetRequiredService<IWorkflowDefinitionCache>();
        var cache2 = provider.GetRequiredService<IWorkflowDefinitionCache>();
        var hostIdentity1 = provider.GetRequiredService<IHostIdentity>();
        var hostIdentity2 = provider.GetRequiredService<IHostIdentity>();
        var correlationKeyResolver1 = provider.GetRequiredService<ICorrelationKeyResolver>();
        var correlationKeyResolver2 = provider.GetRequiredService<ICorrelationKeyResolver>();
        using IServiceScope scope1 = provider.CreateScope();
        using IServiceScope scope2 = provider.CreateScope();
        var registry1 = scope1.ServiceProvider.GetRequiredService<INodeExecutorRegistry>();
        var registry2 = scope2.ServiceProvider.GetRequiredService<INodeExecutorRegistry>();
        var loop1 = scope1.ServiceProvider.GetRequiredService<IBranchLoop>();
        var loop2 = scope2.ServiceProvider.GetRequiredService<IBranchLoop>();
        var bookmarkResumer1 = scope1.ServiceProvider.GetRequiredService<IBookmarkResumer>();
        var bookmarkResumer2 = scope2.ServiceProvider.GetRequiredService<IBookmarkResumer>();
        var triggerConcurrencyEnforcer1 = scope1.ServiceProvider.GetRequiredService<ITriggerConcurrencyEnforcer>();
        var triggerConcurrencyEnforcer2 = scope2.ServiceProvider.GetRequiredService<ITriggerConcurrencyEnforcer>();
        var triggerDispatcher1 = scope1.ServiceProvider.GetRequiredService<ITriggerDispatcher>();
        var triggerDispatcher2 = scope2.ServiceProvider.GetRequiredService<ITriggerDispatcher>();
        var inboundHub1 = scope1.ServiceProvider.GetRequiredService<IInboundHub>();
        var inboundHub2 = scope2.ServiceProvider.GetRequiredService<IInboundHub>();
        var runStarter1 = scope1.ServiceProvider.GetRequiredService<IRunStarter>();
        var runStarter2 = scope2.ServiceProvider.GetRequiredService<IRunStarter>();
        var pendingDrainer1 = scope1.ServiceProvider.GetRequiredService<IPendingTriggerEventDrainer>();
        var pendingDrainer2 = scope2.ServiceProvider.GetRequiredService<IPendingTriggerEventDrainer>();
        var triggerRegistrationService1 = scope1.ServiceProvider.GetRequiredService<ITriggerRegistrationService>();
        var triggerRegistrationService2 = scope2.ServiceProvider.GetRequiredService<ITriggerRegistrationService>();
        var runFinalizer1 = scope1.ServiceProvider.GetRequiredService<IRunFinalizer>();
        var runFinalizer2 = scope2.ServiceProvider.GetRequiredService<IRunFinalizer>();
        var cancellationService1 = scope1.ServiceProvider.GetRequiredService<IRunCancellationService>();
        var cancellationService2 = scope2.ServiceProvider.GetRequiredService<IRunCancellationService>();
        var compensationOrchestrator1 = scope1.ServiceProvider.GetRequiredService<ICompensationOrchestrator>();
        var compensationOrchestrator2 = scope2.ServiceProvider.GetRequiredService<ICompensationOrchestrator>();
        var runCompletedPublisher1 = scope1.ServiceProvider.GetRequiredService<IRunCompletedPublisher>();
        var runCompletedPublisher2 = scope2.ServiceProvider.GetRequiredService<IRunCompletedPublisher>();
        var executors1 = scope1.ServiceProvider.GetServices<INodeExecutor>().ToArray();
        var hostedServices = provider.GetServices<IHostedService>().ToArray();

        // Assert
        Assert.IsType<ChannelRunDispatcher>(dispatcher1);
        Assert.Same(dispatcher1, dispatcher2);
        Assert.IsType<NodeExecutorRegistry>(registry1);
        Assert.NotSame(registry1, registry2);
        Assert.IsType<WorkflowDefinitionCache>(cache1);
        Assert.Same(cache1, cache2);
        Assert.IsType<HostIdentity>(hostIdentity1);
        Assert.Same(hostIdentity1, hostIdentity2);
        Assert.IsType<CorrelationKeyResolver>(correlationKeyResolver1);
        Assert.Same(correlationKeyResolver1, correlationKeyResolver2);
        Assert.IsType<BranchLoop>(loop1);
        Assert.NotSame(loop1, loop2);
        Assert.IsType<BookmarkResumer>(bookmarkResumer1);
        Assert.NotSame(bookmarkResumer1, bookmarkResumer2);
        Assert.IsType<TriggerConcurrencyEnforcer>(triggerConcurrencyEnforcer1);
        Assert.NotSame(triggerConcurrencyEnforcer1, triggerConcurrencyEnforcer2);
        Assert.IsType<TriggerDispatcher>(triggerDispatcher1);
        Assert.NotSame(triggerDispatcher1, triggerDispatcher2);
        Assert.IsType<InboundHub>(inboundHub1);
        Assert.NotSame(inboundHub1, inboundHub2);
        Assert.IsType<RunStarter>(runStarter1);
        Assert.NotSame(runStarter1, runStarter2);
        Assert.IsType<PendingTriggerEventDrainer>(pendingDrainer1);
        Assert.NotSame(pendingDrainer1, pendingDrainer2);
        Assert.IsType<TriggerRegistrationService>(triggerRegistrationService1);
        Assert.NotSame(triggerRegistrationService1, triggerRegistrationService2);
        Assert.IsType<RunFinalizer>(runFinalizer1);
        Assert.NotSame(runFinalizer1, runFinalizer2);
        Assert.IsType<RunCancellationService>(cancellationService1);
        Assert.NotSame(cancellationService1, cancellationService2);
        Assert.IsType<CompensationOrchestrator>(compensationOrchestrator1);
        Assert.NotSame(compensationOrchestrator1, compensationOrchestrator2);
        Assert.IsType<NullRunCompletedPublisher>(runCompletedPublisher1);
        Assert.NotSame(runCompletedPublisher1, runCompletedPublisher2);
        Assert.Contains(executors1, executor => executor.GetType().Name == "DeviceTriggerExecutor");
        Assert.Contains(executors1, executor => executor.GetType().Name == "ScheduleTriggerExecutor");
        Assert.Contains(executors1, executor => executor.GetType().Name == "WebhookTriggerExecutor");
        Assert.Contains(executors1, executor => executor.GetType().Name == "ManualTriggerExecutor");
        Assert.Contains(hostedServices, service => service is BranchExecutionPump);
        Assert.Contains(hostedServices, service => service is BookmarkScheduler);
    }
}
