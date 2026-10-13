using MassTransit;
using Moq;
using Wbskt.Events.Client;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models.Triggers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Engine.Host.InboundAdapters;

namespace Wbskt.Workflow.Engine.Host.Tests.InboundAdapters;

public sealed class ClientPresenceConsumerTests
{
    private static readonly Guid ClientRef = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTime ChangedAt = new(2026, 10, 3, 12, 0, 0, 123, DateTimeKind.Utc);

    [Fact]
    public async Task Disconnect_parks_one_check_per_offline_trigger_due_after_its_grace_period()
    {
        string oneMinute = ClientPresenceTriggerKey.Build(ClientRef, ClientPresenceState.Offline, 60, Guid.NewGuid(), Guid.NewGuid());
        string fiveMinutes = ClientPresenceTriggerKey.Build(ClientRef, ClientPresenceState.Offline, 300, Guid.NewGuid(), Guid.NewGuid());
        string online = ClientPresenceTriggerKey.Build(ClientRef, ClientPresenceState.Online, 0, Guid.NewGuid(), Guid.NewGuid());
        var provider = new RecordingPresenceCheckProvider(oneMinute, fiveMinutes, online);
        var consumer = new ClientDisconnectedConsumer(new ClientPresenceParker(provider));

        await consumer.Consume(Context(new ClientDisconnectedEvent(ClientRef, 12, 34, "Socket closed", "host-1") { CreatedAtUtc = ChangedAt }));

        Assert.Equal([(ClientPresenceTriggerKey.Prefix(ClientRef, ClientPresenceState.Offline), 34)], provider.KeyLookups);
        Assert.Equal(2, provider.Inserts.Count);
        Assert.Contains((oneMinute, ClientRef, "offline", ChangedAt, ChangedAt.AddSeconds(60)), provider.Inserts);
        Assert.Contains((fiveMinutes, ClientRef, "offline", ChangedAt, ChangedAt.AddSeconds(300)), provider.Inserts);
    }

    [Fact]
    public async Task Disconnect_with_no_presence_triggers_parks_nothing()
    {
        var provider = new RecordingPresenceCheckProvider();
        var consumer = new ClientDisconnectedConsumer(new ClientPresenceParker(provider));

        await consumer.Consume(Context(new ClientDisconnectedEvent(ClientRef, 12, 34, "Socket closed", "host-1") { CreatedAtUtc = ChangedAt }));

        Assert.Empty(provider.Inserts);
    }

    [Fact]
    public async Task Connect_parks_online_checks_only()
    {
        string online = ClientPresenceTriggerKey.Build(ClientRef, ClientPresenceState.Online, 0, Guid.NewGuid(), Guid.NewGuid());
        string offline = ClientPresenceTriggerKey.Build(ClientRef, ClientPresenceState.Offline, 60, Guid.NewGuid(), Guid.NewGuid());
        var provider = new RecordingPresenceCheckProvider(online, offline);
        var consumer = new ClientConnectedConsumer(new ClientPresenceParker(provider), new RecentDeviceActivity(TimeProvider.System));

        await consumer.Consume(Context(new ClientConnectedEvent(ClientRef, 12, 34, "host-1") { CreatedAtUtc = ChangedAt }));

        var insert = Assert.Single(provider.Inserts);
        Assert.Equal((online, ClientRef, "online", ChangedAt, ChangedAt), insert);
    }

    private static ConsumeContext<T> Context<T>(T message) where T : class
    {
        var context = new Mock<ConsumeContext<T>>();
        context.SetupGet(c => c.Message).Returns(message);
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        return context.Object;
    }
}
