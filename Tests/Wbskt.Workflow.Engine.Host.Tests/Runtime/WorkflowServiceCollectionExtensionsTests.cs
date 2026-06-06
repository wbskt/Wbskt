using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Wbskt.Workflow.Abstraction.Engine;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Engine.Host.HostedServices;
using Wbskt.Workflow.Extensions;
using Wbskt.Workflow.NodeExecutors.Actions;
using Wbskt.Workflow.NodeExecutors.Controls;
using Wbskt.Workflow.Runtime;
using Wbskt.Workflow.Telemetry;

namespace Wbskt.Workflow.Engine.Host.Tests.Runtime;

public sealed class WorkflowServiceCollectionExtensionsTests
{
    [Fact]
    public void AddWorkflowEngine_registers_full_runtime_without_hosted_services()
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
        services.AddWorkflowEngine(configuration);

        using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        Assert.NotNull(provider.GetRequiredService<IWorkflowDefinitionProvider>());
        Assert.NotNull(provider.GetRequiredService<IWorkflowDefinitionCache>());
        Assert.Empty(provider.GetServices<IHostedService>());
    }

    [Fact]
    public void AddWorkflowRuntime_registers_phase10_services_with_expected_lifetimes()
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
        services.AddHttpClient();
        services.AddWorkflowProviders();
        services.AddWorkflowRuntime();
        services.AddScoped<IDeviceCommandPublisher>(_ => Mock.Of<IDeviceCommandPublisher>());
        services.AddHostedService<BranchExecutionPump>();
        services.AddHostedService<BookmarkScheduler>();
        services.AddHostedService<ScheduledFireTicker>();
        services.AddHostedService<RunReaper>();
        services.AddHostedService<HistoryRetentionGc>();
        services.AddHostedService<PendingTriggerEventBacklogReaper>();
        services.AddHostedService<RunRecoveryService>();
        services.AddHostedService<MetricsExporter>();

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
        var expressionEvaluator1 = provider.GetRequiredService<IExpressionEvaluator>();
        var expressionEvaluator2 = provider.GetRequiredService<IExpressionEvaluator>();
        var leaseHolder1 = provider.GetRequiredService<ILeaseHolder>();
        var leaseHolder2 = provider.GetRequiredService<ILeaseHolder>();
        var workflowMetrics1 = provider.GetRequiredService<WorkflowMetrics>();
        var workflowMetrics2 = provider.GetRequiredService<WorkflowMetrics>();
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
        var runStartedPublisher1 = scope1.ServiceProvider.GetRequiredService<IRunStartedPublisher>();
        var completionHook1 = scope1.ServiceProvider.GetRequiredService<ISubWorkflowCompletionHook>();
        var completionHook2 = scope2.ServiceProvider.GetRequiredService<ISubWorkflowCompletionHook>();
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
        Assert.IsType<ExpressionEvaluator>(expressionEvaluator1);
        Assert.Same(expressionEvaluator1, expressionEvaluator2);
        Assert.True(leaseHolder1.GetType().Name == "AlwaysHoldsLeaseHolder");
        Assert.Same(leaseHolder1, leaseHolder2);
        Assert.IsType<WorkflowMetrics>(workflowMetrics1);
        Assert.Same(workflowMetrics1, workflowMetrics2);
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
        Assert.IsType<NullRunStartedPublisher>(runStartedPublisher1);
        Assert.IsType<SubWorkflowCompletionHook>(completionHook1);
        Assert.NotSame(completionHook1, completionHook2);
        Assert.Contains(executors1, executor => executor.GetType().Name == "DeviceTriggerExecutor");
        Assert.Contains(executors1, executor => executor.GetType().Name == "ScheduleTriggerExecutor");
        Assert.Contains(executors1, executor => executor.GetType().Name == "WebhookTriggerExecutor");
        Assert.Contains(executors1, executor => executor.GetType().Name == "ManualTriggerExecutor");
        Assert.Contains(executors1, executor => executor is LogicNodeExecutor);
        Assert.Contains(executors1, executor => executor is VariableNodeExecutor);
        Assert.Contains(executors1, executor => executor is ForEachNodeExecutor);
        Assert.Contains(executors1, executor => executor is ParallelForEachNodeExecutor);
        Assert.Contains(executors1, executor => executor is DelayNodeExecutor);
        Assert.Contains(executors1, executor => executor is WaitForHttpNodeExecutor);
        Assert.Contains(executors1, executor => executor is AwaitSignalNodeExecutor);
        Assert.Contains(executors1, executor => executor is JoinNodeExecutor);
        Assert.Contains(executors1, executor => executor is SubWorkflowNodeExecutor);
        Assert.Contains(executors1, executor => executor is FailRunNodeExecutor);
        Assert.Contains(executors1, executor => executor is EndNodeExecutor);
        Assert.Contains(executors1, executor => executor is CommandNodeExecutor);
        Assert.Contains(executors1, executor => executor is WebhookNodeExecutor);
        Assert.Contains(executors1, executor => executor is EmailNodeExecutor);
        Assert.Contains(executors1, executor => executor is TelegramNodeExecutor);
        Assert.IsType<LogicNodeExecutor>(registry1.For(NodeKind.ControlLogic));
        Assert.IsType<VariableNodeExecutor>(registry1.For(NodeKind.ControlVariable));
        Assert.IsType<ForEachNodeExecutor>(registry1.For(NodeKind.ControlForEach));
        Assert.IsType<ParallelForEachNodeExecutor>(registry1.For(NodeKind.ControlParallelForEach));
        Assert.IsType<DelayNodeExecutor>(registry1.For(NodeKind.ControlDelay));
        Assert.IsType<WaitForHttpNodeExecutor>(registry1.For(NodeKind.ControlWaitForHttp));
        Assert.IsType<AwaitSignalNodeExecutor>(registry1.For(NodeKind.ControlAwaitSignal));
        Assert.IsType<JoinNodeExecutor>(registry1.For(NodeKind.ControlJoin));
        Assert.IsType<SubWorkflowNodeExecutor>(registry1.For(NodeKind.ControlSubWorkflow));
        Assert.IsType<FailRunNodeExecutor>(registry1.For(NodeKind.ControlFailRun));
        Assert.IsType<EndNodeExecutor>(registry1.For(NodeKind.ControlEnd));
        Assert.IsType<CommandNodeExecutor>(registry1.For(NodeKind.ActionCommand));
        Assert.IsType<WebhookNodeExecutor>(registry1.For(NodeKind.ActionWebhook));
        Assert.IsType<EmailNodeExecutor>(registry1.For(NodeKind.ActionEmail));
        Assert.IsType<TelegramNodeExecutor>(registry1.For(NodeKind.ActionTelegram));
        Assert.Contains(hostedServices, service => service is BranchExecutionPump);
        Assert.Contains(hostedServices, service => service is BookmarkScheduler);
        Assert.Contains(hostedServices, service => service is ScheduledFireTicker);
        Assert.Contains(hostedServices, service => service is RunReaper);
        Assert.Contains(hostedServices, service => service is HistoryRetentionGc);
        Assert.Contains(hostedServices, service => service is PendingTriggerEventBacklogReaper);
        Assert.Contains(hostedServices, service => service is RunRecoveryService);
        Assert.Contains(hostedServices, service => service is MetricsExporter);
    }
}

