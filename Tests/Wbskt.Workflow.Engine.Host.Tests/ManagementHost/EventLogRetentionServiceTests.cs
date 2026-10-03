using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Providers;
using Wbskt.Management.Host.Services;

namespace Wbskt.Workflow.Engine.Host.Tests.ManagementHost;

public sealed class EventLogRetentionServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Sweep_deletes_batches_until_one_comes_back_short()
    {
        var provider = new Mock<IEventProvider>();
        provider.SetupSequence(p => p.DeleteBeforeAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(EventLogRetentionService.BatchSize)
            .ReturnsAsync(EventLogRetentionService.BatchSize)
            .ReturnsAsync(12);

        var total = await CreateService(provider.Object, retentionDays: 90).SweepAsync(CancellationToken.None);

        Assert.Equal(2L * EventLogRetentionService.BatchSize + 12, total);
        var expectedCutoff = Now.UtcDateTime.AddDays(-90);
        provider.Verify(p => p.DeleteBeforeAsync(expectedCutoff, EventLogRetentionService.BatchSize, It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task Sweep_stops_after_one_call_when_nothing_is_old_enough()
    {
        var provider = new Mock<IEventProvider>();
        provider.Setup(p => p.DeleteBeforeAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var total = await CreateService(provider.Object, retentionDays: 7).SweepAsync(CancellationToken.None);

        Assert.Equal(0, total);
        provider.Verify(p => p.DeleteBeforeAsync(Now.UtcDateTime.AddDays(-7), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private static EventLogRetentionService CreateService(IEventProvider provider, int retentionDays)
    {
        var services = new ServiceCollection().AddSingleton(provider).BuildServiceProvider();
        return new EventLogRetentionService(
            services.GetRequiredService<IServiceScopeFactory>(),
            new FixedTimeProvider(Now),
            Options.Create(new EventLoggingOptions { RetentionDays = retentionDays }),
            NullLogger<EventLogRetentionService>.Instance);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
