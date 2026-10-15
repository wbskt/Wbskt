using Wbskt.Workflow.Abstraction.Enums;
using System.Text.Json;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Workflow;
using Wbskt.Infrastructure.Security;
using Wbskt.Management.Host.Providers;
using Wbskt.Management.Host.Services.Clients;
using Wbskt.Management.Host.Services.Integrations;
using Wbskt.Management.Host.Services.Workflow;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Models.Triggers;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Abstraction.Validation;
using Wbskt.Workflow.Engine.Host.InboundAdapters;
using Wbskt.Workflow.Engine.Host.Providers;

namespace Wbskt.Host.Tests.InboundAdapters;

/// <summary>
/// The engine's side of a <see cref="WorkflowDefinitionChangedEvent"/>: what the consumer drops, and
/// that a trigger change made through the management host is what the engine reads after one event.
/// </summary>
public sealed class WorkflowDefinitionChangedConsumerTests
{
    private const int WorkspaceId = 34;
    private static readonly Guid ClientRef = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid WorkflowRef = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public async Task Consume_evicts_the_named_definitions_and_every_cached_registration()
    {
        var definitions = new Mock<IWorkflowDefinitionCache>();
        var registrations = new Mock<ITriggerRegistrationCache>();

        await new WorkflowDefinitionChangedConsumer(definitions.Object, registrations.Object)
            .Consume(Context(new WorkflowDefinitionChangedEvent(WorkflowRef, WorkspaceId, [11, 12])));

        definitions.Verify(c => c.Invalidate(11), Times.Once);
        definitions.Verify(c => c.Invalidate(12), Times.Once);
        definitions.VerifyNoOtherCalls();
        registrations.Verify(c => c.Clear(), Times.Once);
    }

    [Fact]
    public async Task A_retired_workspace_clears_the_registrations_too()
    {
        var registrations = new Mock<ITriggerRegistrationCache>();

        await new WorkflowDefinitionChangedConsumer(Mock.Of<IWorkflowDefinitionCache>(), registrations.Object)
            .Consume(Context(new WorkflowDefinitionChangedEvent(null, WorkspaceId, [])));

        registrations.Verify(c => c.Clear(), Times.Once);
    }

    [Fact]
    public async Task A_trigger_reinstated_on_the_management_host_is_seen_by_the_engine_after_one_event()
    {
        // The engine has cached "no hold trigger listens for this device". The workflow is then
        // reinstated in the management host, which registers the trigger and announces the change.
        var holds = new MutableHoldStateProvider();
        var engine = new EngineSide(holds);
        await engine.ReceiveAsync();
        await engine.ReceiveAsync();
        Assert.Equal(1, holds.Lookups);
        Assert.Empty(holds.Records);

        var management = new ManagementSide(isEnabled: false, onRegistered: () => holds.Registrations = [HoldRegistration()]);
        var reinstated = await management.Service.ReinstateAsync(WorkspaceId, Guid.NewGuid(), WorkflowRef, CancellationToken.None);
        Assert.True(reinstated.IsSuccess);

        // Until the event arrives the engine still answers from its cache...
        await engine.ReceiveAsync();
        Assert.Empty(holds.Records);

        // ...and the one event is all it takes for the next message to reach the new trigger.
        await engine.DeliverAsync(management.Bus.Single<WorkflowDefinitionChangedEvent>());
        await engine.ReceiveAsync();

        Assert.Equal(2, holds.Lookups);
        Assert.Single(holds.Records);
        engine.Definitions.Verify(c => c.Invalidate(ManagementSide.DefinitionId), Times.Once);
    }

    [Fact]
    public async Task A_trigger_deprecated_on_the_management_host_is_dropped_by_the_engine_after_one_event()
    {
        var holds = new MutableHoldStateProvider { Registrations = [HoldRegistration()] };
        var engine = new EngineSide(holds);
        await engine.ReceiveAsync();
        Assert.Single(holds.Records);

        var management = new ManagementSide(isEnabled: true, onDeregistered: () => holds.Registrations = []);
        var deprecated = await management.Service.DeprecateAsync(WorkspaceId, WorkflowRef, CancellationToken.None);
        Assert.True(deprecated.IsSuccess);

        await engine.DeliverAsync(management.Bus.Single<WorkflowDefinitionChangedEvent>());
        await engine.ReceiveAsync();

        Assert.Equal(2, holds.Lookups);
        Assert.Single(holds.Records);
    }

    [Fact]
    public async Task The_engine_hosts_consumer_scan_gives_the_event_a_queue()
    {
        // Registered the way AddRabbitMqEventBus registers the engine's consumers: an assembly scan,
        // which names the endpoint after the consumer (the engine sets no name formatter of its own).
        var services = new ServiceCollection();
        services.AddMassTransit(x =>
        {
            x.AddConsumers(typeof(WorkflowDefinitionChangedConsumer).Assembly);
            x.UsingInMemory((context, cfg) => cfg.ConfigureEndpoints(context));
        });
        await using ServiceProvider provider = services.BuildServiceProvider();
        var bus = provider.GetRequiredService<IBusControl>();

        await bus.StartAsync();
        string endpoints = JsonSerializer.Serialize(bus.GetProbeResult());
        await bus.StopAsync();

        // The endpoint's name, not the message type's (that one ends in "Event").
        Assert.Contains("WorkflowDefinitionChanged\"", endpoints);
    }

    private static ConsumeContext<WorkflowDefinitionChangedEvent> Context(WorkflowDefinitionChangedEvent message)
    {
        var context = new Mock<ConsumeContext<WorkflowDefinitionChangedEvent>>();
        context.SetupGet(c => c.Message).Returns(message);
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        return context.Object;
    }

    private static TriggerRegistrationRow HoldRegistration() => new()
    {
        Id = 1,
        WorkflowDefinitionId = ManagementSide.DefinitionId,
        WorkflowRefId = WorkflowRef,
        WorkflowVersion = 1,
        TriggerNodeId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
        TriggerKind = ClientHoldTriggerKey.TriggerKind,
        TriggerKey = ClientHoldTriggerKey.Build(ClientRef, "temperature", 60, WorkflowRef, Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc")),
        CorrelationExpression = null,
        ConcurrencyPolicy = TriggerRegistrationRow.FormatConcurrencyPolicy(WorkflowConcurrencyPolicy.Queue),
        FilterExpression = null,
        CreatedAt = new DateTime(2026, 10, 14, 0, 0, 0, DateTimeKind.Utc)
    };

    /// <summary>The engine host: its registration cache, the hold recorder that reads through it, and the consumer.</summary>
    private sealed class EngineSide
    {
        private readonly ClientHoldRecorder _recorder;
        private readonly WorkflowDefinitionChangedConsumer _consumer;
        private int _messages;

        public EngineSide(MutableHoldStateProvider holds)
        {
            var cache = global::Wbskt.Host.Tests.Runtime.TriggerRegistrationCacheTests.CreateCache();
            _recorder = new ClientHoldRecorder(holds, new FixedFilterEvaluator(true), cache);
            _consumer = new WorkflowDefinitionChangedConsumer(Definitions.Object, cache);
        }

        public Mock<IWorkflowDefinitionCache> Definitions { get; } = new();

        public Task ReceiveAsync()
        {
            _messages++;
            DateTime sentAt = new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc).AddSeconds(_messages);
            InboundEvent message = new("client", ["client:" + ClientRef + ":temperature"], "m-" + _messages, new Dictionary<string, JsonElement>(), sentAt);
            return _recorder.RecordAsync(message, ClientRef, "temperature", WorkspaceId, sentAt, CancellationToken.None);
        }

        public Task DeliverAsync(WorkflowDefinitionChangedEvent changed) => _consumer.Consume(Context(changed));
    }

    /// <summary>The management host's lifecycle service over a workflow row, with the trigger writes stood in for.</summary>
    private sealed class ManagementSide
    {
        public const int DefinitionId = 40;

        public ManagementSide(bool isEnabled, Action? onRegistered = null, Action? onDeregistered = null)
        {
            var definitions = new Mock<IWorkflowDefinitionProvider>();
            definitions.Setup(p => p.FindCurrentByRefIdAsync(WorkflowRef, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new WorkflowDefinitionRow
                {
                    Id = DefinitionId,
                    RefId = WorkflowRef,
                    Version = 1,
                    WorkspaceId = WorkspaceId,
                    Name = "Freezer alarm",
                    Description = null,
                    IsEnabled = isEnabled,
                    // Nothing in it is checked here; reinstating only re-checks the integrations it names.
                    DefinitionJson = "null",
                    PublishedBy = 7,
                    CreatedAt = new DateTime(2026, 10, 14, 0, 0, 0, DateTimeKind.Utc)
                });

            var triggers = new Mock<ITriggerRegistrationService>();
            triggers.Setup(t => t.OnPublishedAsync(DefinitionId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .Callback(() => onRegistered?.Invoke())
                .Returns(Task.CompletedTask);
            triggers.Setup(t => t.OnDeprecatedAsync(DefinitionId, It.IsAny<CancellationToken>()))
                .Callback(() => onDeregistered?.Invoke())
                .Returns(Task.CompletedTask);

            var identity = new Mock<IIdentityService>();
            identity.Setup(i => i.GetUserIdentity()).Returns(new UserIdentity(7));

            Service = new WorkflowLifecycleService(
                definitions.Object,
                triggers.Object,
                new WorkflowValidator(),
                new IntegrationReferenceCheck(Mock.Of<IIntegrationProvider>()),
                identity.Object,
                Mock.Of<IWorkflowEngineGateway>(),
                Bus,
                Mock.Of<ILogger<WorkflowLifecycleService>>());
        }

        public RecordingBus Bus { get; } = new();

        public WorkflowLifecycleService Service { get; }
    }

    private sealed class RecordingBus : IEventBus
    {
        private readonly List<IEvent> _published = [];

        public Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default) where TEvent : IEvent
        {
            _published.Add(@event);
            return Task.CompletedTask;
        }

        public TEvent Single<TEvent>() where TEvent : IEvent => Assert.Single(_published.OfType<TEvent>());
    }

    /// <summary>The hold registrations of a test's one client, which the management side changes.</summary>
    private sealed class MutableHoldStateProvider : IClientHoldStateProvider
    {
        public TriggerRegistrationRow[] Registrations { get; set; } = [];

        public int Lookups { get; private set; }

        public List<string> Records { get; } = [];

        public Task<IReadOnlyCollection<TriggerRegistrationRow>> GetRegistrationsAsync(Guid clientRefId, string messageType, int workspaceId, CancellationToken ct)
        {
            Lookups++;
            IReadOnlyCollection<TriggerRegistrationRow> answer = Registrations.ToArray();
            return Task.FromResult(answer);
        }

        public Task RecordAsync(string triggerKey, bool matches, DateTime eventAt, int holdSeconds, string payload, CancellationToken ct)
        {
            Records.Add(triggerKey);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyCollection<ClientHoldStateRow>> LeaseDueAsync(int leaseSec, int batch, CancellationToken ct) =>
            Task.FromResult<IReadOnlyCollection<ClientHoldStateRow>>([]);

        public Task MarkFiredAsync(int id, DateTime sinceAt, CancellationToken ct) => Task.CompletedTask;

        public Task DeleteByIdAsync(int id, CancellationToken ct) => Task.CompletedTask;
    }
}
