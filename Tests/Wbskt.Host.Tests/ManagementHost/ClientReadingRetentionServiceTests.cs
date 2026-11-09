using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Wbskt.Infrastructure.Billing;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Providers;
using Wbskt.Management.Host.Services.Readings;
using Wbskt.Models.Billing;

namespace Wbskt.Host.Tests.ManagementHost;

public sealed class ClientReadingRetentionServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Sweep_compacts_batches_until_none_comes_back_full()
    {
        var provider = new Mock<IClientReadingProvider>();
        provider.SetupSequence(p => p.CompactAsync(It.IsAny<DateTime>(), It.IsAny<IReadOnlyCollection<ReadingRetention>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReadingCompaction(ClientReadingRetentionService.BatchSize, 3, 0))
            .ReturnsAsync(new ReadingCompaction(10, ClientReadingRetentionService.BatchSize, 0))
            .ReturnsAsync(new ReadingCompaction(2, 1, 4));

        var total = await CreateService(provider.Object).SweepAsync(CancellationToken.None);

        Assert.Equal(new ReadingCompaction(ClientReadingRetentionService.BatchSize + 12, ClientReadingRetentionService.BatchSize + 4, 4), total);
        provider.Verify(p => p.CompactAsync(Now.UtcDateTime, It.IsAny<IReadOnlyCollection<ReadingRetention>>(), ClientReadingRetentionService.BatchSize, It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task Each_plan_keeps_readings_for_its_own_days_with_the_configurations_changes()
    {
        var provider = new Mock<IClientReadingProvider>();
        IReadOnlyCollection<ReadingRetention>? sent = null;
        provider.Setup(p => p.CompactAsync(It.IsAny<DateTime>(), It.IsAny<IReadOnlyCollection<ReadingRetention>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<DateTime, IReadOnlyCollection<ReadingRetention>, int, CancellationToken>((_, r, _, _) => sent = r)
            .ReturnsAsync(new ReadingCompaction(0, 0, 0));
        var catalog = new PlanCatalog(Options.Create(new PlanCatalogOptions
        {
            Plans = new() { [PlanCode.Maker] = new PlanLimitOverrides { RawReadingsDays = 21 } }
        }));

        await CreateService(provider.Object, catalog).SweepAsync(CancellationToken.None);

        Assert.NotNull(sent);
        Assert.Contains(new ReadingRetention(PlanCode.Free, 3, 30), sent);
        Assert.Contains(new ReadingRetention(PlanCode.Maker, 21, 365), sent);
        Assert.Contains(new ReadingRetention(PlanCode.Pro, 30, 730), sent);
        Assert.Equal(Enum.GetValues<PlanCode>().Length, sent.Count);
    }

    private static ClientReadingRetentionService CreateService(IClientReadingProvider provider, PlanCatalog? catalog = null)
    {
        var services = new ServiceCollection().AddSingleton(provider).BuildServiceProvider();
        return new ClientReadingRetentionService(
            services.GetRequiredService<IServiceScopeFactory>(),
            new FixedTimeProvider(Now),
            Options.Create(new ReadingsOptions()),
            catalog ?? new PlanCatalog(Options.Create(new PlanCatalogOptions())),
            NullLogger<ClientReadingRetentionService>.Instance);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
