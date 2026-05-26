using Microsoft.Extensions.Logging;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Workflow.Engine.Host.Tests.Runtime;

public sealed class InboundHubTests
{
    [Fact]
    public async Task Handle_stamps_received_at_when_default()
    {
        // Arrange
        var dispatcher = new RecordingTriggerDispatcher();
        var hub = new InboundHub(dispatcher, new FixedClock(), new RecordingLogger());
        InboundEvent evt = new("device", string.Empty, "evt-1", new Dictionary<string, System.Text.Json.JsonElement>(), default);

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
        InboundEvent evt = new("device", "key-1", "evt-1", new Dictionary<string, System.Text.Json.JsonElement>(), DateTime.UtcNow);

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
        TriggerDispatchResult actual = await hub.HandleAsync(new InboundEvent("device", string.Empty, "evt-1", new Dictionary<string, System.Text.Json.JsonElement>(), DateTime.UtcNow), CancellationToken.None);

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
        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => hub.HandleAsync(new InboundEvent("device", string.Empty, "evt-1", new Dictionary<string, System.Text.Json.JsonElement>(), DateTime.UtcNow), CancellationToken.None));

        // Assert
        Assert.Equal("boom", ex.Message);
        Assert.Contains(logger.Messages, message => message.Contains("evt-1", StringComparison.Ordinal));
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

    private sealed class FixedClock : IClock
    {
        public DateTime UtcNow => new(2026, 5, 26, 12, 30, 0, DateTimeKind.Utc);
    }

    private sealed class RecordingLogger : ILogger<InboundHub>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }
    }
}
