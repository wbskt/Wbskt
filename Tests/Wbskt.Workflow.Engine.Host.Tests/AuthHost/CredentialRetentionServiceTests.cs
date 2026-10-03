using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.Auth.Host.Providers;
using Wbskt.Auth.Host.Services;

namespace Wbskt.Workflow.Engine.Host.Tests.AuthHost;

public sealed class CredentialRetentionServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Sweep_repeats_until_a_pass_deletes_nothing_with_a_cutoff_a_grace_period_back()
    {
        var provider = new Mock<ICredentialRetentionProvider>();
        provider.SetupSequence(p => p.DeleteExpiredAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(7000)
            .ReturnsAsync(12)
            .ReturnsAsync(0);
        var services = new ServiceCollection().AddSingleton(provider.Object).BuildServiceProvider();
        var service = new CredentialRetentionService(
            services.GetRequiredService<IServiceScopeFactory>(), new FixedTimeProvider(Now), NullLogger<CredentialRetentionService>.Instance);

        var total = await service.SweepAsync(CancellationToken.None);

        Assert.Equal(7012, total);
        provider.Verify(p => p.DeleteExpiredAsync(Now.UtcDateTime - CredentialRetentionService.Grace, CredentialRetentionService.BatchSize, It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
