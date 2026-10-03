using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.Auth.Host.Services.Events;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Auth;

namespace Wbskt.Workflow.Engine.Host.Tests.AuthHost;

/// <summary>
/// The queue that keeps RabbitMQ out of the sign-in path: publishing returns before the broker is
/// involved, events keep their own type on the way through, and one failed publish does not stop
/// the ones after it.
/// </summary>
public sealed class QueuedEventBusTests
{
    [Fact]
    public async Task Publishing_returns_without_touching_the_real_bus()
    {
        var queue = new QueuedEventBus(NullLogger<QueuedEventBus>.Instance);

        await queue.PublishAsync(new TokenRotatedEvent(1, Guid.NewGuid(), "10.0.0.1"));

        Assert.True(queue.TryRead(out var queued));
        Assert.Equal(nameof(TokenRotatedEvent), queued!.Name);
    }

    [Fact]
    public async Task The_dispatcher_publishes_each_event_as_its_own_type_and_survives_a_failure()
    {
        var queue = new QueuedEventBus(NullLogger<QueuedEventBus>.Instance);
        var bus = new Mock<IEventBus>();
        var delivered = new TaskCompletionSource();
        bus.Setup(b => b.PublishAsync(It.IsAny<UserLoginFailedEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("broker down"));
        bus.Setup(b => b.PublishAsync(It.IsAny<TokenRotatedEvent>(), It.IsAny<CancellationToken>()))
            .Callback(() => delivered.TrySetResult())
            .Returns(Task.CompletedTask);

        var dispatcher = new QueuedEventDispatcher(queue, bus.Object, NullLogger<QueuedEventDispatcher>.Instance);
        await dispatcher.StartAsync(CancellationToken.None);

        await queue.PublishAsync(new UserLoginFailedEvent(1, Guid.NewGuid(), "10.0.0.1", "Invalid password"));
        await queue.PublishAsync(new TokenRotatedEvent(1, Guid.NewGuid(), "10.0.0.1"));

        await delivered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await dispatcher.StopAsync(CancellationToken.None);

        bus.Verify(b => b.PublishAsync(It.IsAny<TokenRotatedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Events_still_queued_at_shutdown_are_sent_before_stopping()
    {
        var queue = new QueuedEventBus(NullLogger<QueuedEventBus>.Instance);
        var bus = new Mock<IEventBus>();
        var running = new TaskCompletionSource();
        bus.Setup(b => b.PublishAsync(It.IsAny<UserLoginSuccessEvent>(), It.IsAny<CancellationToken>()))
            .Callback(() => running.TrySetResult())
            .Returns(Task.CompletedTask);
        var dispatcher = new QueuedEventDispatcher(queue, bus.Object, NullLogger<QueuedEventDispatcher>.Instance);

        // Wait until the loop is actually reading, then stop straight after queuing two more:
        // whatever the loop has not reached by then is left to the shutdown drain, and either way
        // nothing queued before stopping is lost.
        await dispatcher.StartAsync(CancellationToken.None);
        await queue.PublishAsync(new UserLoginSuccessEvent(1, Guid.NewGuid(), "10.0.0.1"));
        await running.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await queue.PublishAsync(new TokenRotatedEvent(1, Guid.NewGuid(), "10.0.0.1"));
        await queue.PublishAsync(new TokenRotatedEvent(2, Guid.NewGuid(), "10.0.0.1"));
        await dispatcher.StopAsync(CancellationToken.None);

        bus.Verify(b => b.PublishAsync(It.IsAny<TokenRotatedEvent>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }
}
