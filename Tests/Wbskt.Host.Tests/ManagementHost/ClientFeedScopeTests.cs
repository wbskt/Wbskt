using System.Reflection;
using FluentAssertions;
using MassTransit;
using Microsoft.AspNetCore.SignalR;
using Moq;
using Wbskt.Events;
using Wbskt.Events.Abstractions;
using Wbskt.Events.Client;
using Wbskt.Management.Host.Handlers;
using Wbskt.Management.Host.Hubs;

namespace Wbskt.Host.Tests.ManagementHost;

public sealed class ClientFeedScopeTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);
    private const int ClientId = 7;
    private const int WorkspaceId = 3;

    [Fact]
    public void Every_client_scoped_event_names_its_client()
    {
        var scoped = ForwardedEvents().Where(t => Notify(t).Scope == NotifyScope.Client).ToList();

        scoped.Should().Contain([typeof(ClientMessageReceivedEvent), typeof(ClientCommandDeliveredEvent), typeof(ClientCommandAckedEvent)]);
        scoped.Should().AllSatisfy(t => typeof(IClientContext).IsAssignableFrom(t).Should().BeTrue(t.Name));
    }

    // Lists and the explorer show these, so every tab in the workspace still needs them.
    [Theory]
    [InlineData(typeof(ClientConnectedEvent))]
    [InlineData(typeof(ClientDisconnectedEvent))]
    [InlineData(typeof(ClientStatusChangedEvent))]
    [InlineData(typeof(ClientRenamedEvent))]
    [InlineData(typeof(ClientLatencyMeasuredEvent))]
    public void Lifecycle_events_stay_workspace_wide(Type eventType)
    {
        Notify(eventType).Scope.Should().Be(NotifyScope.Workspace);
    }

    [Fact]
    public async Task A_device_message_goes_to_its_watchers_not_the_workspace()
    {
        var hub = new FakeHub();
        var handler = new SignalRForwardingHandler<ClientMessageReceivedEvent>(hub.Context, new ClientFeedThrottle(new FixedTimeProvider(Now)));

        await handler.Consume(Context(Message()));

        hub.Sent.Should().ContainSingle().Which.Should().Be((NotificationHub.ClientGroupName(ClientId), "OnClientMessageReceivedEvent"));
    }

    [Fact]
    public async Task A_workspace_event_still_goes_to_the_workspace()
    {
        var hub = new FakeHub();
        var handler = new SignalRForwardingHandler<ClientRenamedEvent>(hub.Context, new ClientFeedThrottle(new FixedTimeProvider(Now)));
        var renamed = new ClientRenamedEvent(Guid.NewGuid(), ClientId, WorkspaceId, "old", "new");

        await handler.Consume(Context(renamed));

        hub.Sent.Should().ContainSingle().Which.Group.Should().Be(NotificationHub.GroupName(WorkspaceId));
    }

    [Fact]
    public async Task A_fast_device_is_capped_and_the_watchers_are_told_how_many_were_skipped()
    {
        var hub = new FakeHub();
        var time = new FixedTimeProvider(Now);
        var handler = new SignalRForwardingHandler<ClientMessageReceivedEvent>(hub.Context, new ClientFeedThrottle(time));

        for (var i = 0; i < ClientFeedThrottle.MaxPerSecond + 5; i++)
        {
            await handler.Consume(Context(Message()));
        }

        hub.Sent.Should().HaveCount(ClientFeedThrottle.MaxPerSecond);

        time.Now = Now.AddSeconds(1);
        await handler.Consume(Context(Message()));

        hub.Sent.Skip(ClientFeedThrottle.MaxPerSecond).Select(s => s.Method).Should()
            .Equal(NotificationHub.ClientFeedSkippedMethod, "OnClientMessageReceivedEvent");
        hub.Payloads.OfType<ClientFeedSkipped>().Should().ContainSingle().Which.Skipped.Should().Be(5);
    }

    [Fact]
    public void The_cap_is_per_device()
    {
        var throttle = new ClientFeedThrottle(new FixedTimeProvider(Now));

        for (var i = 0; i < ClientFeedThrottle.MaxPerSecond; i++)
        {
            throttle.TryAcquire(1, out _).Should().BeTrue();
        }

        throttle.TryAcquire(1, out _).Should().BeFalse();
        throttle.TryAcquire(2, out _).Should().BeTrue();
    }

    private static ClientMessageReceivedEvent Message() => new(Guid.NewGuid(), ClientId, WorkspaceId, "temp", "{}");

    private static IEnumerable<Type> ForwardedEvents() => typeof(SignalRNotifyAttribute).Assembly.GetTypes()
        .Where(t => t.IsClass && t.GetCustomAttribute<SignalRNotifyAttribute>() != null);

    private static SignalRNotifyAttribute Notify(Type t) => t.GetCustomAttribute<SignalRNotifyAttribute>()!;

    private static ConsumeContext<T> Context<T>(T message) where T : class
    {
        var context = new Mock<ConsumeContext<T>>();
        context.SetupGet(c => c.Message).Returns(message);
        return context.Object;
    }

    private sealed class FakeHub
    {
        public List<(string Group, string Method)> Sent { get; } = [];
        public List<object?> Payloads { get; } = [];
        public IHubContext<NotificationHub> Context { get; }

        public FakeHub()
        {
            var clients = new Mock<IHubClients>();
            clients.Setup(c => c.Group(It.IsAny<string>())).Returns((string group) =>
            {
                var proxy = new Mock<IClientProxy>();
                proxy.Setup(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
                    .Callback((string method, object?[] args, CancellationToken _) =>
                    {
                        Sent.Add((group, method));
                        Payloads.AddRange(args);
                    })
                    .Returns(Task.CompletedTask);
                return proxy.Object;
            });

            var context = new Mock<IHubContext<NotificationHub>>();
            context.SetupGet(c => c.Clients).Returns(clients.Object);
            Context = context.Object;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
