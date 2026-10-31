using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Runtime;
using Wbskt.Workflow.Telemetry;

namespace Wbskt.Host.Tests.Runtime;

public sealed class InboundHubTests
{
    [Fact]
    public async Task Handle_stamps_received_at_when_default()
    {
        // Arrange
        var dispatcher = new RecordingTriggerDispatcher();
        var hub = new InboundHub(dispatcher, new FixedClock(), new RecordingLogger());
        InboundEvent evt = new("client", [], "evt-1", new Dictionary<string, System.Text.Json.JsonElement>(), default);

        // Act
        await hub.HandleAsync(evt, CancellationToken.None);

        // Assert
        Assert.Equal(new DateTime(2026, 5, 26, 12, 30, 0, DateTimeKind.Utc), dispatcher.Events.Single().ReceivedAt);
    }

    [Fact]
    public async Task Handle_delegates_to_dispatcher()
    {
        // Arrange
        var dispatcher = new RecordingTriggerDispatcher();
        var hub = new InboundHub(dispatcher, new FixedClock(), new RecordingLogger());
        InboundEvent evt = new("client", ["key-1"], "evt-1", new Dictionary<string, System.Text.Json.JsonElement>(), DateTime.UtcNow);

        // Act
        await hub.HandleAsync(evt, CancellationToken.None);

        // Assert
        Assert.Single(dispatcher.Events);
        Assert.Equal("evt-1", dispatcher.Events.Single().InboundEventId);
    }

    [Fact]
    public async Task Handle_returns_dispatcher_result()
    {
        // Arrange
        TriggerDispatchResult expected = new(TriggerDispatchOutcome.StartedRun, 42, null, "ok");
        var hub = new InboundHub(new RecordingTriggerDispatcher(expected), new FixedClock(), new RecordingLogger());

        // Act
        TriggerDispatchResult actual = await hub.HandleAsync(new InboundEvent("client", [], "evt-1", new Dictionary<string, System.Text.Json.JsonElement>(), DateTime.UtcNow), CancellationToken.None);

        // Assert
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task Handle_logs_and_rethrows_on_unexpected_exception()
    {
        // Arrange
        var logger = new RecordingLogger();
        var hub = new InboundHub(new ThrowingTriggerDispatcher(), new FixedClock(), logger);

        // Act
        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => hub.HandleAsync(new InboundEvent("client", [], "evt-1", new Dictionary<string, System.Text.Json.JsonElement>(), DateTime.UtcNow), CancellationToken.None));

        // Assert
        Assert.Equal("boom", ex.Message);
        Assert.Contains(logger.Messages, message => message.Contains("evt-1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Handle_logs_nothing_at_information_or_above_for_an_event_that_matches_nothing()
    {
        // Arrange
        var logger = new RecordingLogger();
        var hub = new InboundHub(new RecordingTriggerDispatcher(), new FixedClock(), logger);

        // Act
        await hub.HandleAsync(new InboundEvent("client", ["key-1"], "evt-1", new Dictionary<string, System.Text.Json.JsonElement>(), DateTime.UtcNow), CancellationToken.None);

        // Assert
        Assert.DoesNotContain(logger.Levels, level => level >= LogLevel.Information);
    }

    [Fact]
    public async Task Handle_counts_each_event_by_channel_and_outcome()
    {
        // Arrange
        using var metrics = new WorkflowMetrics();
        using var counts = new InboundEventCounts(metrics);
        var hub = new InboundHub(new RecordingTriggerDispatcher(), new FixedClock(), new RecordingLogger(), metrics: metrics);

        // Act
        await hub.HandleAsync(new InboundEvent("client", [], "evt-1", new Dictionary<string, System.Text.Json.JsonElement>(), DateTime.UtcNow), CancellationToken.None);
        await hub.HandleAsync(new InboundEvent("client", [], "evt-2", new Dictionary<string, System.Text.Json.JsonElement>(), DateTime.UtcNow), CancellationToken.None);

        // Assert
        Assert.Equal(2, counts.Get("client", nameof(TriggerDispatchOutcome.NoRegistration)));
    }

    [Fact]
    public async Task Handle_counts_a_failed_event()
    {
        // Arrange
        using var metrics = new WorkflowMetrics();
        using var counts = new InboundEventCounts(metrics);
        var hub = new InboundHub(new ThrowingTriggerDispatcher(), new FixedClock(), new RecordingLogger(), metrics: metrics);

        // Act
        await Assert.ThrowsAsync<InvalidOperationException>(() => hub.HandleAsync(new InboundEvent("client", [], "evt-1", new Dictionary<string, System.Text.Json.JsonElement>(), DateTime.UtcNow), CancellationToken.None));

        // Assert
        Assert.Equal(1, counts.Get("client", "Failed"));
    }

    private sealed class InboundEventCounts : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly Dictionary<(string Channel, string Outcome), long> _counts = [];

        public InboundEventCounts(WorkflowMetrics metrics)
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (ReferenceEquals(instrument, metrics.InboundEvents))
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>((_, measurement, tags, _) =>
            {
                string channel = "", outcome = "";
                foreach (KeyValuePair<string, object?> tag in tags)
                {
                    if (tag.Key == "channel") channel = (string)tag.Value!;
                    if (tag.Key == "outcome") outcome = (string)tag.Value!;
                }
                _counts[(channel, outcome)] = _counts.GetValueOrDefault((channel, outcome)) + measurement;
            });
            _listener.Start();
        }

        public long Get(string channel, string outcome) => _counts.GetValueOrDefault((channel, outcome));

        public void Dispose() => _listener.Dispose();
    }

    private sealed class RecordingTriggerDispatcher(TriggerDispatchResult? result = null) : ITriggerDispatcher
    {
        public List<InboundEvent> Events { get; } = [];

        public Task<TriggerDispatchResult> DispatchAsync(InboundEvent evt, CancellationToken ct)
        {
            Events.Add(evt);
            return Task.FromResult(result ?? new TriggerDispatchResult(TriggerDispatchOutcome.NoRegistration, null, null, "none"));
        }
    }

    private sealed class ThrowingTriggerDispatcher : ITriggerDispatcher
    {
        public Task<TriggerDispatchResult> DispatchAsync(InboundEvent evt, CancellationToken ct)
        {
            throw new InvalidOperationException("boom");
        }
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => UtcNow;

        public DateTime UtcNow => new(2026, 5, 26, 12, 30, 0, DateTimeKind.Utc);
    }

    private sealed class RecordingLogger : ILogger<InboundHub>
    {
        public List<string> Messages { get; } = [];
        public List<LogLevel> Levels { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
            Levels.Add(logLevel);
        }
    }
}
