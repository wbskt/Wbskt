using System.Text.Json;
using Moq;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Channels;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Host.Tests.Runtime;

/// <summary>
/// How the dispatcher reads a device message's trigger registrations through the cache: "nothing
/// listens" is served from it, anything else is read again before it can claim the event or start a
/// run, and a change announcement makes a newly published trigger visible to the next message.
/// </summary>
public sealed class TriggerDispatcherRegistrationCacheTests
{
    private static readonly Guid ClientRef = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly string TelemetryKey = ClientChannel.Key(ClientRef.ToString(), "telemetry");

    [Fact]
    public async Task A_device_message_nothing_listens_for_is_answered_from_the_cache_the_next_time()
    {
        var harness = new Harness();

        TriggerDispatchResult first = await harness.Dispatcher.DispatchAsync(DeviceMessage("m-1"), CancellationToken.None);
        TriggerDispatchResult second = await harness.Dispatcher.DispatchAsync(DeviceMessage("m-2"), CancellationToken.None);

        Assert.Equal(TriggerDispatchOutcome.NoRegistration, first.Outcome);
        Assert.Equal(TriggerDispatchOutcome.NoRegistration, second.Outcome);
        Assert.Equal(1, harness.Registrations.Reads);
        // Nothing is claimed for either: the look-before-claim answer is the same, only cheaper.
        Assert.Equal([false, false], harness.Bookmarks.HasRegistrations);
    }

    [Fact]
    public async Task After_a_change_is_announced_the_next_device_message_reaches_the_trigger_published_since()
    {
        var harness = new Harness();
        await harness.Dispatcher.DispatchAsync(DeviceMessage("m-1"), CancellationToken.None);

        harness.Registrations.Rows = [Registration(5, TelemetryKey)];
        harness.Cache.Clear();
        TriggerDispatchResult result = await harness.Dispatcher.DispatchAsync(DeviceMessage("m-2"), CancellationToken.None);

        Assert.Equal(TriggerDispatchOutcome.StartedRun, result.Outcome);
        Assert.Equal(2, harness.Registrations.Reads);
        harness.RunStarter.Verify(s => s.StartAsync(105, It.IsAny<string>(), It.IsAny<InboundEvent>(), It.IsAny<string?>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
    }

    [Fact]
    public async Task A_cached_registration_is_read_again_and_a_removed_one_starts_nothing()
    {
        // Deprecated in the management host, announcement not yet here: the cache still holds the
        // registration, and the dispatcher must not act on it.
        var harness = new Harness(Registration(5, TelemetryKey));
        TriggerDispatchResult first = await harness.Dispatcher.DispatchAsync(DeviceMessage("m-1"), CancellationToken.None);

        harness.Registrations.Rows = [];
        TriggerDispatchResult second = await harness.Dispatcher.DispatchAsync(DeviceMessage("m-2"), CancellationToken.None);

        Assert.Equal(TriggerDispatchOutcome.StartedRun, first.Outcome);
        Assert.Equal(TriggerDispatchOutcome.NoRegistration, second.Outcome);
        Assert.Equal(2, harness.Registrations.Reads);
        Assert.Equal([true, false], harness.Bookmarks.HasRegistrations);
        harness.RunStarter.Verify(s => s.StartAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<InboundEvent>(), It.IsAny<string?>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
    }

    [Fact]
    public async Task Other_channels_are_read_every_time()
    {
        // A run started by hand right after a publish must not wait on the announcement.
        var harness = new Harness();
        Guid workflowRef = Guid.NewGuid();

        await harness.Dispatcher.DispatchAsync(ManualChannel.Event(workflowRef, JsonSerializer.SerializeToElement(new { }), "r-1", DateTime.UtcNow), CancellationToken.None);
        await harness.Dispatcher.DispatchAsync(ManualChannel.Event(workflowRef, JsonSerializer.SerializeToElement(new { }), "r-2", DateTime.UtcNow), CancellationToken.None);

        Assert.Equal(2, harness.Registrations.Reads);
    }

    private static InboundEvent DeviceMessage(string messageId) => ClientChannel.Event(
        new ClientMessage(ClientRef, 12, 34, "telemetry", JsonSerializer.SerializeToElement(new { celsius = 21 }), DateTime.UtcNow, DateTime.UtcNow, false),
        $"client-message:{ClientRef}:{messageId}");

    private static TriggerRegistrationRow Registration(int id, string triggerKey) =>
        TriggerRegistrationCacheTests.Registration(id, ClientChannel.Name, triggerKey);

    private sealed class Harness
    {
        public Harness(params TriggerRegistrationRow[] rows)
        {
            Registrations = new CountingRegistrationProvider(rows);
            RunStarter.Setup(s => s.StartAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<InboundEvent>(), It.IsAny<string?>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
                .ReturnsAsync(() => new RunStartResult(1, Guid.NewGuid(), 2, true));
            var enforcer = new Mock<ITriggerConcurrencyEnforcer>();
            enforcer.Setup(e => e.EvaluateAsync(It.IsAny<TriggerRegistrationRow>(), It.IsAny<InboundEvent>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new TriggerConcurrencyDecision(TriggerConcurrencyOutcome.Proceed, []));

            Dispatcher = new TriggerDispatcher(
                new CorrelationKeyResolver(TriggerChannelRegistry.Default),
                Bookmarks,
                Registrations,
                Cache,
                enforcer.Object,
                new NoOpRunCancellationService(),
                RunStarter.Object,
                Mock.Of<IRunDispatcher>(),
                new InMemoryIdempotencyKeyProvider(),
                Mock.Of<IExpressionEvaluator>());
        }

        public TriggerRegistrationCache Cache { get; } = TriggerRegistrationCacheTests.CreateCache();

        public CountingRegistrationProvider Registrations { get; }

        public NoBookmarks Bookmarks { get; } = new();

        public Mock<IRunStarter> RunStarter { get; } = new();

        public TriggerDispatcher Dispatcher { get; }
    }

    /// <summary>No bookmark ever matches; records what the dispatcher told it about registrations.</summary>
    private sealed class NoBookmarks : IBookmarkResumer
    {
        public List<bool> HasRegistrations { get; } = [];

        public Task<BookmarkMatchResult> MatchInboundAsync(InboundEvent evt, bool hasRegistrations, CancellationToken ct)
        {
            HasRegistrations.Add(hasRegistrations);
            return Task.FromResult(new BookmarkMatchResult(false, null, false));
        }
    }

    /// <summary>dbo.TriggerRegistration_GetAllByKeys over rows a test can change, counting the reads.</summary>
    private sealed class CountingRegistrationProvider(TriggerRegistrationRow[] rows) : ITriggerRegistrationProvider
    {
        public TriggerRegistrationRow[] Rows { get; set; } = rows;

        public int Reads { get; private set; }

        public Task<IReadOnlyCollection<TriggerRegistrationRow>> GetActiveByChannelKeysAsync(string channelKind, IReadOnlyCollection<string> channelKeys, CancellationToken ct)
        {
            Reads++;
            IReadOnlyCollection<TriggerRegistrationRow> matches = Rows.Where(r => r.TriggerKind == channelKind && channelKeys.Contains(r.TriggerKey)).ToArray();
            return Task.FromResult(matches);
        }

        public Task<TriggerRegistrationRow> InsertAsync(TriggerRegistrationRow row, CancellationToken ct) => throw new NotSupportedException();

        public Task DeleteAllByWorkflowDefinitionIdAsync(int workflowDefinitionId, CancellationToken ct) => throw new NotSupportedException();
    }
}
