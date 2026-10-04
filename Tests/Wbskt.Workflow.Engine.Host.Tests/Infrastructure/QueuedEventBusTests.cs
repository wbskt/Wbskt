using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Auth;
using Wbskt.Infrastructure.Events;

namespace Wbskt.Workflow.Engine.Host.Tests.Infrastructure;

/// <summary>
/// The queue that keeps RabbitMQ out of the request path: publishing returns before the broker is
/// involved, events keep their own type on the way through, a failed publish is retried until the
/// broker answers, and one that never goes through does not stop the ones after it.
/// </summary>
public sealed class QueuedEventBusTests
{
    private static readonly TimeSpan[] FastRetries = [TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(10)];

    [Fact]
    public async Task Publishing_returns_without_touching_the_real_bus()
    {
        var queue = new QueuedEventBus(NullLogger<QueuedEventBus>.Instance);

        await queue.PublishAsync(new TokenRotatedEvent(1, Guid.NewGuid(), "10.0.0.1"));

        Assert.True(queue.TryRead(out var queued));
        Assert.Equal(nameof(TokenRotatedEvent), queued!.Name);
    }

    [Fact]
    public async Task A_publish_that_fails_is_retried_until_the_broker_answers()
    {
        var queue = new QueuedEventBus(NullLogger<QueuedEventBus>.Instance);
        var bus = new Mock<IEventBus>();
        var delivered = new TaskCompletionSource();
        var attempts = 0;
        bus.Setup(b => b.PublishAsync(It.IsAny<TokenRotatedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                if (++attempts < 3)
                {
                    return Task.FromException(new InvalidOperationException("broker down"));
                }

                delivered.TrySetResult();
                return Task.CompletedTask;
            });

        var dispatcher = new QueuedEventDispatcher(queue, bus.Object, NullLogger<QueuedEventDispatcher>.Instance, FastRetries);
        await dispatcher.StartAsync(CancellationToken.None);

        await queue.PublishAsync(new TokenRotatedEvent(1, Guid.NewGuid(), "10.0.0.1"));

        await delivered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await dispatcher.StopAsync(CancellationToken.None);

        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task An_event_that_never_goes_through_is_given_up_on_and_the_next_one_is_sent()
    {
        var queue = new QueuedEventBus(NullLogger<QueuedEventBus>.Instance);
        var bus = new Mock<IEventBus>();
        var delivered = new TaskCompletionSource();
        bus.Setup(b => b.PublishAsync(It.IsAny<UserLoginFailedEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("cannot be serialized"));
        bus.Setup(b => b.PublishAsync(It.IsAny<TokenRotatedEvent>(), It.IsAny<CancellationToken>()))
            .Callback(() => delivered.TrySetResult())
            .Returns(Task.CompletedTask);

        var dispatcher = new QueuedEventDispatcher(queue, bus.Object, NullLogger<QueuedEventDispatcher>.Instance, FastRetries);
        await dispatcher.StartAsync(CancellationToken.None);

        await queue.PublishAsync(new UserLoginFailedEvent(1, Guid.NewGuid(), "10.0.0.1", "Invalid password"));
        await queue.PublishAsync(new TokenRotatedEvent(1, Guid.NewGuid(), "10.0.0.1"));

        await delivered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await dispatcher.StopAsync(CancellationToken.None);

        // The first attempt plus one per retry delay, then it is dropped.
        bus.Verify(b => b.PublishAsync(It.IsAny<UserLoginFailedEvent>(), It.IsAny<CancellationToken>()), Times.Exactly(FastRetries.Length + 1));
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
