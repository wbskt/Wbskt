using Microsoft.Extensions.DependencyInjection;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Providers;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Workflow.Extensions;

public static class WorkflowServiceCollectionExtensions
{
    public static IServiceCollection AddWorkflowProviders(this IServiceCollection services)
    {
        services.AddScoped<IWorkflowDefinitionProvider, WorkflowDefinitionProvider>();
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
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IIdGenerator, GuidIdGenerator>();
        services.AddSingleton<ICreditCostCalculator, DefaultCreditCostCalculator>();

        return services;
    }
}
