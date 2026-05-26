using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Wbskt.Workflow.Abstraction.Configuration;
using Wbskt.Workflow.Abstraction.Engine;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Engine;
using Wbskt.Workflow.NodeExecutors.Actions;
using Wbskt.Workflow.NodeExecutors.Controls;
using Wbskt.Workflow.NodeExecutors.Triggers;
using Wbskt.Workflow.Providers;
using Wbskt.Workflow.Runtime;
using Wbskt.Workflow.Telemetry;

namespace Wbskt.Workflow.Extensions;

public static class WorkflowServiceCollectionExtensions
{
    public static IServiceCollection AddWorkflowProviders(this IServiceCollection services)
    {
        services.AddSingleton<IWorkflowDefinitionProvider, WorkflowDefinitionProvider>();
        services.AddScoped<ITriggerRegistrationProvider, TriggerRegistrationProvider>();
        services.AddScoped<IRunProvider, RunProvider>();
        services.AddScoped<IRunCountersProvider, RunCountersProvider>();
        services.AddScoped<IBranchProvider, BranchProvider>();
        services.AddScoped<IBookmarkProvider, BookmarkProvider>();
        services.AddScoped<IHistoryEventProvider, HistoryEventProvider>();
        services.AddScoped<IIdempotencyKeyProvider, IdempotencyKeyProvider>();
        services.AddScoped<ISharedVariableProvider, SharedVariableProvider>();
        services.AddScoped<IPendingTriggerEventProvider, PendingTriggerEventProvider>();
        services.AddScoped<IScheduledFireProvider, ScheduledFireProvider>();

        return services;
    }

    public static IServiceCollection AddWorkflowRuntime(this IServiceCollection services, IConfiguration configuration)
    {
        WorkflowEngineOptions options = configuration.GetSection("WorkflowEngine").Get<WorkflowEngineOptions>() ?? new WorkflowEngineOptions();
        services.AddSingleton(Options.Create(options));
        return AddWorkflowRuntime(services);
    }

    public static IServiceCollection AddWorkflowRuntime(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IIdGenerator, GuidIdGenerator>();
        services.AddSingleton<ICreditCostCalculator, DefaultCreditCostCalculator>();
        services.AddSingleton<IHostIdentity, HostIdentity>();
        services.AddSingleton<ChannelRunDispatcher>();
        services.AddSingleton<IRunDispatcher>(serviceProvider => serviceProvider.GetRequiredService<ChannelRunDispatcher>());
        services.AddSingleton<IWorkflowDefinitionCache, WorkflowDefinitionCache>();
        services.AddSingleton<ICorrelationKeyResolver, CorrelationKeyResolver>();
        services.AddSingleton<IExpressionEvaluator, ExpressionEvaluator>();
        services.AddScoped<INodeExecutor, DeviceTriggerExecutor>();
        services.AddScoped<INodeExecutor, ScheduleTriggerExecutor>();
        services.AddScoped<INodeExecutor, WebhookTriggerExecutor>();
        services.AddScoped<INodeExecutor, ManualTriggerExecutor>();
        services.AddScoped<INodeExecutor, LogicNodeExecutor>();
        services.AddScoped<INodeExecutor, VariableNodeExecutor>();
        services.AddScoped<INodeExecutor, ForEachNodeExecutor>();
        services.AddScoped<INodeExecutor, DelayNodeExecutor>();
        services.AddScoped<INodeExecutor, WaitForHttpNodeExecutor>();
        services.AddScoped<INodeExecutor, AwaitSignalNodeExecutor>();
        services.AddScoped<INodeExecutor, JoinNodeExecutor>();
        services.AddScoped<INodeExecutor, SubWorkflowNodeExecutor>();
        services.AddScoped<INodeExecutor, FailRunNodeExecutor>();
        services.AddScoped<INodeExecutor, EndNodeExecutor>();
        services.AddScoped<INodeExecutor, CommandNodeExecutor>();
        services.AddScoped<INodeExecutor, WebhookNodeExecutor>();
        services.AddScoped<INodeExecutor, EmailNodeExecutor>();
        services.AddScoped<INodeExecutor, TelegramNodeExecutor>();
        services.AddScoped<INodeExecutorRegistry, NodeExecutorRegistry>();
        services.AddScoped<IProviderComposite, RuntimeProviderComposite>();
        services.AddScoped<IBranchLoop, BranchLoop>();
        services.AddScoped<IBookmarkResumer, BookmarkResumer>();
        services.AddScoped<ITriggerConcurrencyEnforcer, TriggerConcurrencyEnforcer>();
        services.AddScoped<IRunFinalizer, RunFinalizer>();
        services.AddScoped<IRunCancellationService, RunCancellationService>();
        services.AddScoped<ICompensationOrchestrator, CompensationOrchestrator>();
        services.AddScoped<IRunCompletedPublisher, NullRunCompletedPublisher>();
        services.AddScoped<IRunStarter, RunStarter>();
        services.AddScoped<ITriggerDispatcher, TriggerDispatcher>();
        services.AddScoped<IInboundHub, InboundHub>();
        services.AddScoped<IPendingTriggerEventDrainer, PendingTriggerEventDrainer>();
        services.AddScoped<ITriggerRegistrationService, TriggerRegistrationService>();

        return services;
    }
}
