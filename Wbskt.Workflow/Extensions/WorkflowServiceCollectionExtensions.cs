using Microsoft.Extensions.DependencyInjection;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.NodeExecutors.Triggers;
using Wbskt.Workflow.Providers;
using Wbskt.Workflow.Runtime;

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
        services.AddScoped<INodeExecutor, DeviceTriggerExecutor>();
        services.AddScoped<INodeExecutor, ScheduleTriggerExecutor>();
        services.AddScoped<INodeExecutor, WebhookTriggerExecutor>();
        services.AddScoped<INodeExecutor, ManualTriggerExecutor>();
        services.AddScoped<INodeExecutorRegistry, NodeExecutorRegistry>();
        services.AddScoped<IProviderComposite, RuntimeProviderComposite>();
        services.AddScoped<IBranchLoop, BranchLoop>();
        services.AddScoped<IBookmarkResumer, BookmarkResumer>();
        services.AddScoped<ITriggerConcurrencyEnforcer, TriggerConcurrencyEnforcer>();
        services.AddScoped<IRunCancellationService, NoOpRunCancellationService>();
        services.AddScoped<IRunStarter, RunStarter>();
        services.AddScoped<ITriggerDispatcher, TriggerDispatcher>();
        services.AddScoped<IInboundHub, InboundHub>();
        services.AddScoped<IPendingTriggerEventDrainer, PendingTriggerEventDrainer>();
        services.AddScoped<ITriggerRegistrationService, TriggerRegistrationService>();

        return services;
    }
}
