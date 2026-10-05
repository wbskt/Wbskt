using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Providers;
using Wbskt.Management.Host.Services;
using Wbskt.Management.Host.Services.Events;

namespace Wbskt.Workflow.Engine.Host.Tests.ManagementHost;

public sealed class EventLogRetentionServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Sweep_deletes_batches_until_one_comes_back_short()
    {
        var provider = new Mock<IEventProvider>();
        provider.SetupSequence(p => p.DeleteBeforeAsync(It.IsAny<DateTime>(), It.IsAny<int>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(EventLogRetentionService.BatchSize)
            .ReturnsAsync(EventLogRetentionService.BatchSize)
            .ReturnsAsync(12);

        var total = await CreateService(provider.Object, new EventRegistry(), retentionDays: 90).SweepAsync(CancellationToken.None);

        Assert.Equal(2L * EventLogRetentionService.BatchSize + 12, total);
        var expectedCutoff = Now.UtcDateTime.AddDays(-90);
        provider.Verify(p => p.DeleteBeforeAsync(expectedCutoff, EventLogRetentionService.BatchSize, null, It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task Sweep_stops_after_one_call_when_nothing_is_old_enough()
    {
        var provider = new Mock<IEventProvider>();
        provider.Setup(p => p.DeleteBeforeAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<IReadOnlyCollection<int>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var total = await CreateService(provider.Object, new EventRegistry(), retentionDays: 7).SweepAsync(CancellationToken.None);

        Assert.Equal(0, total);
        provider.Verify(p => p.DeleteBeforeAsync(Now.UtcDateTime.AddDays(-7), It.IsAny<int>(), null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Device_traffic_goes_after_its_own_shorter_window_and_audit_events_after_theirs()
    {
        var registry = new EventRegistry();
        registry.RegisterEvent("ClientMessageReceivedEvent", 11);
        registry.RegisterEvent("ClientPongEvent", 12);
        registry.RegisterEvent("PolicyCreatedEvent", 20);
        var provider = new Mock<IEventProvider>();
        provider.Setup(p => p.DeleteBeforeAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<IReadOnlyCollection<int>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);

        var total = await CreateService(provider.Object, registry, retentionDays: 90, deviceTrafficRetentionDays: 14).SweepAsync(CancellationToken.None);

        Assert.Equal(6, total);
        provider.Verify(p => p.DeleteBeforeAsync(
            Now.UtcDateTime.AddDays(-14), It.IsAny<int>(),
            It.Is<IReadOnlyCollection<int>?>(ids => ids != null && ids.OrderBy(i => i).SequenceEqual(new[] { 11, 12 })),
            It.IsAny<CancellationToken>()), Times.Once);
        provider.Verify(p => p.DeleteBeforeAsync(Now.UtcDateTime.AddDays(-90), It.IsAny<int>(), null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task No_separate_device_traffic_pass_when_its_window_is_not_shorter()
    {
        var registry = new EventRegistry();
        registry.RegisterEvent("ClientMessageReceivedEvent", 11);
        var provider = new Mock<IEventProvider>();
        provider.Setup(p => p.DeleteBeforeAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<IReadOnlyCollection<int>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        await CreateService(provider.Object, registry, retentionDays: 7, deviceTrafficRetentionDays: 14).SweepAsync(CancellationToken.None);

        provider.Verify(p => p.DeleteBeforeAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsNotNull<IReadOnlyCollection<int>?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void Device_traffic_is_device_chatter_not_actions_people_take()
    {
        Assert.Contains("ClientMessageReceivedEvent", DeviceTrafficAttribute.EventNames);
        Assert.Contains("ClientCommandDeliveredEvent", DeviceTrafficAttribute.EventNames);
        // Sending a command or a ping is something a person did, so it stays in the audit log.
        Assert.DoesNotContain("ClientCommandEvent", DeviceTrafficAttribute.EventNames);
        Assert.DoesNotContain("ClientPingEvent", DeviceTrafficAttribute.EventNames);
        Assert.DoesNotContain("ClientConnectedEvent", DeviceTrafficAttribute.EventNames);
    }

    private static EventLogRetentionService CreateService(IEventProvider provider, IEventRegistry registry, int retentionDays, int deviceTrafficRetentionDays = 14)
    {
        var services = new ServiceCollection().AddSingleton(provider).AddSingleton(registry).BuildServiceProvider();
        return new EventLogRetentionService(
            services.GetRequiredService<IServiceScopeFactory>(),
            new FixedTimeProvider(Now),
            Options.Create(new EventLoggingOptions { RetentionDays = retentionDays, DeviceTrafficRetentionDays = deviceTrafficRetentionDays }),
            NullLogger<EventLogRetentionService>.Instance);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
