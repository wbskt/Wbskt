using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Wbskt.Workflow.Abstraction.Configuration;
using Wbskt.Workflow.Abstraction.Engine;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Channels;
using Wbskt.Workflow.Engine;
using Wbskt.Workflow.Integrations;
using Wbskt.Workflow.NodeExecutors.Actions;
using Wbskt.Workflow.NodeExecutors.Controls;
using Wbskt.Workflow.NodeExecutors.Triggers;
using Wbskt.Workflow.Providers;
using Wbskt.Workflow.Runtime;
using Wbskt.Workflow.Runtime.BranchResults;
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
        // Only what management reads or writes: definitions and their trigger registrations (with the
        // schedule rows a registration writes), and the run, branch, history and shared-variable reads
        // behind the console. Bookmarks, idempotency keys, pending trigger events, joins, leases,
        // counters and retention are the engine's alone.
        services.AddSingleton<IWorkflowDefinitionProvider, WorkflowDefinitionProvider>();
        services.AddScoped<ITriggerRegistrationProvider, TriggerRegistrationProvider>();
        services.AddScoped<IScheduledFireProvider, ScheduledFireProvider>();
        services.AddScoped<IRunProvider, RunProvider>();
        services.AddScoped<IBranchProvider, BranchProvider>();
        services.AddScoped<IHistoryEventProvider, HistoryEventProvider>();
        services.AddScoped<ISharedVariableProvider, SharedVariableProvider>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<ITriggerChannelRegistry>(TriggerChannelRegistry.Default);
        services.AddScoped<ITriggerRegistrationService, TriggerRegistrationService>();

        // No IRunCancellationService, token registry or definition cache: the engine is the only writer
        // of run state, so management asks it to cancel (a CancelWorkflowRun command) rather than running
        // the cancellation state machine itself, and a cache here would only ever be this host's own copy.

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
        services.AddScoped<IRunRetentionProvider, RunRetentionProvider>();
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

        // The master keys and transport rules for integrations. The credentials themselves are each
        // workspace's own, sealed in dbo.Integrations - never host configuration, never on a node.
        services.AddSingleton(Options.Create(configuration.GetSection(IntegrationOptions.SectionName).Get<IntegrationOptions>() ?? new IntegrationOptions()));

        return AddWorkflowRuntime(services);
    }

    public static IServiceCollection AddWorkflowRuntime(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IIdGenerator, GuidIdGenerator>();
        services.AddSingleton<ICreditCostCalculator, DefaultCreditCostCalculator>();
        services.AddSingleton<IHostIdentity, HostIdentity>();
        services.AddSingleton<ILeadershipGate, AlwaysActiveLeadershipGate>();
        // The engine host re-registers ILeadershipGate as LeadershipGate (last registration wins);
        // Management/tests keep the always-true default since they never run engine hosted services.
        services.AddSingleton<LeadershipState>();
        services.AddSingleton<WorkflowMetrics>();
        services.AddSingleton<ChannelRunDispatcher>();
        services.AddSingleton<IRunDispatcher>(serviceProvider => serviceProvider.GetRequiredService<ChannelRunDispatcher>());
        services.AddSingleton<IWorkflowDefinitionCache, WorkflowDefinitionCache>();
        // Process-wide, like the definition cache: the engine host's WorkflowDefinitionChangedConsumer
        // clears both when the management host announces a change.
        services.AddSingleton<ITriggerRegistrationCache, TriggerRegistrationCache>();
        services.AddSingleton<ITriggerChannelRegistry>(TriggerChannelRegistry.Default);
        services.AddSingleton<ICorrelationKeyResolver, CorrelationKeyResolver>();
        // Scoped, not Singleton: the evaluator reads shared variables through the Scoped
        // ISharedVariableProvider. Every consumer (the Logic/Variable/ForEach/ParallelForEach
        // executors) is itself Scoped, so this does not widen anything's lifetime.
        services.AddScoped<IExpressionEvaluator, ExpressionEvaluator>();
        services.AddSingleton<IOutboundAddressGuard, OutboundAddressGuard>();
        services.TryAddSingleton(Options.Create(new IntegrationOptions()));
        services.AddSingleton<IntegrationCipher>();
        services.AddSingleton<TelegramChannel>();
        services.AddSingleton<DiscordChannel>();
        services.AddSingleton<SmtpChannel>();
        services.AddSingleton<NtfyChannel>();
        services.AddSingleton<HttpAuthChannel>();
        services.AddSingleton<IIntegrationChannel>(sp => sp.GetRequiredService<TelegramChannel>());
        services.AddSingleton<IIntegrationChannel>(sp => sp.GetRequiredService<DiscordChannel>());
        services.AddSingleton<IIntegrationChannel>(sp => sp.GetRequiredService<SmtpChannel>());
        services.AddSingleton<IIntegrationChannel>(sp => sp.GetRequiredService<NtfyChannel>());
        services.AddSingleton<IIntegrationChannel>(sp => sp.GetRequiredService<HttpAuthChannel>());
        services.AddSingleton<IntegrationVault>();
        services.AddSingleton<IIntegrationVault>(sp => sp.GetRequiredService<IntegrationVault>());
        services.AddSingleton<IIntegrationStore, IntegrationStore>();
        services.AddScoped<IIntegrationResolver, IntegrationResolver>();
        // The client/schedule/webhook/manual triggers all just record the payload and continue via
        // "default", so a single PassthroughTriggerExecutor serves them - one instance per kind.
        services.AddScoped<INodeExecutor>(sp => new PassthroughTriggerExecutor(NodeKind.TriggerClient, sp.GetRequiredService<TimeProvider>()));
        services.AddScoped<INodeExecutor>(sp => new PassthroughTriggerExecutor(NodeKind.TriggerClientPresence, sp.GetRequiredService<TimeProvider>()));
        services.AddScoped<INodeExecutor>(sp => new PassthroughTriggerExecutor(NodeKind.TriggerSchedule, sp.GetRequiredService<TimeProvider>()));
        services.AddScoped<INodeExecutor>(sp => new PassthroughTriggerExecutor(NodeKind.TriggerWebhook, sp.GetRequiredService<TimeProvider>()));
        services.AddScoped<INodeExecutor>(sp => new PassthroughTriggerExecutor(NodeKind.TriggerManual, sp.GetRequiredService<TimeProvider>()));
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
        services.AddScoped<INodeExecutor, DiscordNodeExecutor>();
        services.AddScoped<INodeExecutor, NtfyNodeExecutor>();
        services.AddScoped<INodeExecutorRegistry, NodeExecutorRegistry>();
        // One handler per NodeExecutionResult type, as there is one executor per node kind. Scoped like
        // the executors and holding nothing per branch: each call brings its branch in a BranchResultContext.
        // The branch endings and the Continue handler are shared rather than built inside the classes
        // that use them: the loop and three handlers end branches, and the Fail handler continues one
        // when an onFailure routes it to a port.
        services.AddScoped<BranchEndings>();
        services.AddScoped<ContinueResultHandler>();
        services.AddScoped<IBranchResultHandler>(sp => sp.GetRequiredService<ContinueResultHandler>());
        services.AddScoped<IBranchResultHandler, ForkResultHandler>();
        services.AddScoped<IBranchResultHandler, WaitForBookmarkResultHandler>();
        services.AddScoped<IBranchResultHandler, FailResultHandler>();
        services.AddScoped<IBranchResultHandler, TerminalResultHandler>();
        services.AddScoped<IBranchResultDispatcher, BranchResultDispatcher>();
        services.AddScoped<IBranchLoop, BranchLoop>();
        services.AddScoped<IStrandedBranchFailer, StrandedBranchFailer>();
        services.AddScoped<IBookmarkResumer, BookmarkResumer>();
        services.AddScoped<ITriggerConcurrencyEnforcer, TriggerConcurrencyEnforcer>();
        services.AddScoped<IRunFinalizer, RunFinalizer>();
        // The token registry is process-wide - the service that reads it is per-scope.
        services.AddSingleton<RunCancellationTokenRegistry>();
        services.AddScoped<IRunCancellationService, RunCancellationService>();
        services.AddScoped<IRunJournal, RunJournal>();
        services.AddScoped<IBranchTerminator, BranchTerminator>();
        // For the branch terminator, whose finalizer leads back to it (see BranchTerminator).
        services.AddScoped(sp => new Lazy<IRunFinalizer>(sp.GetRequiredService<IRunFinalizer>));
        services.AddScoped<ICompensationOrchestrator, CompensationOrchestrator>();
        services.AddScoped<IRunCompletedPublisher, NullRunCompletedPublisher>();
        services.AddScoped<IRunStartedPublisher, NullRunStartedPublisher>();
        services.AddScoped<IRunStarter, RunStarter>();
        services.AddScoped<ITriggerDispatcher, TriggerDispatcher>();
        services.AddScoped<ITriggerFilterEvaluator, TriggerFilterEvaluator>();
        services.AddScoped<IInboundHub, InboundHub>();
        services.AddScoped<ISubWorkflowCompletionHook, SubWorkflowCompletionHook>();
        services.AddScoped<IPendingTriggerEventDrainer, PendingTriggerEventDrainer>();
        services.AddScoped<ITriggerRegistrationService, TriggerRegistrationService>();
        services.TryAddSingleton<IEngineStartupTracker, CompletedEngineStartupTracker>();

        return services;
    }
}
