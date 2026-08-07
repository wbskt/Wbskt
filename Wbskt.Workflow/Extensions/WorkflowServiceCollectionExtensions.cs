using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Wbskt.Workflow.Abstraction.Configuration;
using Wbskt.Workflow.Abstraction.Engine;
using Wbskt.Workflow.Abstraction.Models.Nodes;
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
    public static IServiceCollection AddWorkflowEngine(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddWorkflowProviders();
        services.AddWorkflowRuntime(configuration);
        return services;
    }

    public static IServiceCollection AddWorkflowManagementServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddWorkflowProviders();
        services.AddMemoryCache();
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IWorkflowDefinitionCache, WorkflowDefinitionCache>();
        services.AddScoped<ITriggerRegistrationService, TriggerRegistrationService>();
        services.AddScoped<IRunCancellationService, RunCancellationService>();

        WorkflowEngineOptions options = configuration.GetSection("WorkflowEngine").Get<WorkflowEngineOptions>() ?? new WorkflowEngineOptions();
        services.AddSingleton(Options.Create(options));

        return services;
    }

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
        services.AddScoped<IJoinAggregatorProvider, JoinAggregatorProvider>();
        services.AddScoped<ILeaseProvider, LeaseProvider>();

        return services;
    }

    public static IServiceCollection AddWorkflowRuntime(this IServiceCollection services, IConfiguration configuration)
    {
        WorkflowEngineOptions options = configuration.GetSection("WorkflowEngine").Get<WorkflowEngineOptions>() ?? new WorkflowEngineOptions();
        services.AddSingleton(Options.Create(options));

        // Pricing is an operator decision that changes without a release, so the cost table is bound
        // from configuration; anything not overridden keeps CreditCostOptions.BuiltIn.
        CreditCostOptions creditCosts = configuration.GetSection("WorkflowEngine:CreditCosts").Get<CreditCostOptions>() ?? new CreditCostOptions();
        services.AddSingleton(Options.Create(creditCosts));

        // Credentials for the notification channels. Bound here, never carried on a node - a definition
        // is readable by the whole workspace and frozen into every published version.
        services.AddSingleton(Options.Create(configuration.GetSection("WorkflowEngine:Email").Get<EmailOptions>() ?? new EmailOptions()));
        services.AddSingleton(Options.Create(configuration.GetSection("WorkflowEngine:Telegram").Get<TelegramOptions>() ?? new TelegramOptions()));

        return AddWorkflowRuntime(services);
    }

    public static IServiceCollection AddWorkflowRuntime(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IIdGenerator, GuidIdGenerator>();
        services.AddSingleton<ICreditCostCalculator, DefaultCreditCostCalculator>();
        services.AddSingleton<IHostIdentity, HostIdentity>();
        services.AddSingleton<ILeaseHolder, AlwaysHoldsLeaseHolder>();
        // The engine host re-registers ILeaseHolder as SqlLeaseHolder (last registration wins);
        // Management/tests keep the always-true default since they never run engine hosted services.
        services.AddSingleton<LeadershipState>();
        services.AddSingleton<WorkflowMetrics>();
        services.AddSingleton<ChannelRunDispatcher>();
        services.AddSingleton<IRunDispatcher>(serviceProvider => serviceProvider.GetRequiredService<ChannelRunDispatcher>());
        services.AddSingleton<IWorkflowDefinitionCache, WorkflowDefinitionCache>();
        services.AddSingleton<ICorrelationKeyResolver, CorrelationKeyResolver>();
        // Scoped, not Singleton: the evaluator reads shared variables through the Scoped
        // ISharedVariableProvider. Every consumer (the Logic/Variable/ForEach/ParallelForEach
        // executors) is itself Scoped, so this does not widen anything's lifetime.
        services.AddScoped<IExpressionEvaluator, ExpressionEvaluator>();
        services.AddSingleton<IOutboundAddressGuard, OutboundAddressGuard>();
        services.AddSingleton<IEmailSender, SmtpEmailSender>();
        // The client/schedule/webhook/manual triggers all just record the payload and continue via
        // "default", so a single PassthroughTriggerExecutor serves them - one instance per kind.
        services.AddScoped<INodeExecutor>(sp => new PassthroughTriggerExecutor(NodeKind.TriggerClient, sp.GetRequiredService<IClock>()));
        services.AddScoped<INodeExecutor>(sp => new PassthroughTriggerExecutor(NodeKind.TriggerSchedule, sp.GetRequiredService<IClock>()));
        services.AddScoped<INodeExecutor>(sp => new PassthroughTriggerExecutor(NodeKind.TriggerWebhook, sp.GetRequiredService<IClock>()));
        services.AddScoped<INodeExecutor>(sp => new PassthroughTriggerExecutor(NodeKind.TriggerManual, sp.GetRequiredService<IClock>()));
        services.AddScoped<INodeExecutor, LogicNodeExecutor>();
        services.AddScoped<INodeExecutor, VariableNodeExecutor>();
        services.AddScoped<INodeExecutor, ForEachNodeExecutor>();
        services.AddScoped<INodeExecutor, ParallelForEachNodeExecutor>();
        services.AddScoped<INodeExecutor, ForkNodeExecutor>();
        services.AddScoped<INodeExecutor, DelayNodeExecutor>();
        services.AddScoped<INodeExecutor, WaitForHttpNodeExecutor>();
        services.AddScoped<INodeExecutor, AwaitSignalNodeExecutor>();
        services.AddScoped<INodeExecutor, JoinNodeExecutor>();
        services.AddScoped<INodeExecutor, SubWorkflowNodeExecutor>();
        services.AddScoped<INodeExecutor, FailRunNodeExecutor>();
        services.AddScoped<INodeExecutor, EndNodeExecutor>();
        services.AddScoped<INodeExecutor, CommandNodeExecutor>();
        services.AddScoped<INodeExecutor, WebhookNodeExecutor>();
        services.AddScoped<INodeExecutor, ToastNodeExecutor>();
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
        services.AddScoped<IRunStartedPublisher, NullRunStartedPublisher>();
        services.AddScoped<IRunStarter, RunStarter>();
        services.AddScoped<ITriggerDispatcher, TriggerDispatcher>();
        services.AddScoped<IInboundHub, InboundHub>();
        services.AddScoped<ISubWorkflowCompletionHook, SubWorkflowCompletionHook>();
        services.AddScoped<IPendingTriggerEventDrainer, PendingTriggerEventDrainer>();
        services.AddScoped<ITriggerRegistrationService, TriggerRegistrationService>();
        services.TryAddSingleton<IEngineStartupTracker, CompletedEngineStartupTracker>();

        return services;
    }
}
