using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Providers;
using Wbskt.Management.Host.Services.Readings;

namespace Wbskt.Workflow.Engine.Host.Tests.ManagementHost;

public sealed class ClientReadingRetentionServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Sweep_deletes_batches_until_one_comes_back_short()
    {
        var provider = new Mock<IClientReadingProvider>();
        provider.SetupSequence(p => p.DeleteBeforeAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ClientReadingRetentionService.BatchSize)
            .ReturnsAsync(3);

        var total = await CreateService(provider.Object, new ReadingsOptions()).SweepAsync(CancellationToken.None);

        Assert.Equal(ClientReadingRetentionService.BatchSize + 3L, total);
        provider.Verify(p => p.DeleteBeforeAsync(Now.UtcDateTime.AddDays(-365), ClientReadingRetentionService.BatchSize, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Retention_follows_the_configured_days()
    {
        var provider = new Mock<IClientReadingProvider>();

        await CreateService(provider.Object, new ReadingsOptions { RetentionDays = 30 }).SweepAsync(CancellationToken.None);

        provider.Verify(p => p.DeleteBeforeAsync(Now.UtcDateTime.AddDays(-30), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private static ClientReadingRetentionService CreateService(IClientReadingProvider provider, ReadingsOptions options)
    {
        var services = new ServiceCollection().AddSingleton(provider).BuildServiceProvider();
        return new ClientReadingRetentionService(
            services.GetRequiredService<IServiceScopeFactory>(),
            new FixedTimeProvider(Now),
            Options.Create(options),
            NullLogger<ClientReadingRetentionService>.Instance);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
