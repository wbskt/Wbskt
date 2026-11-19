using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.Auth.Host.Providers;
using Wbskt.Auth.Host.Services;

namespace Wbskt.Host.Tests.AuthHost;

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
        provider.Setup(p => p.AnnounceExpiredInvitationsAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        provider.Setup(p => p.GetUnconfirmedAccountsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var services = new ServiceCollection().AddSingleton(provider.Object).AddSingleton(Mock.Of<IAccountDeletionService>()).BuildServiceProvider();
        var service = new CredentialRetentionService(
            services.GetRequiredService<IServiceScopeFactory>(), new FixedTimeProvider(Now), NullLogger<CredentialRetentionService>.Instance);

        var total = await service.SweepAsync(CancellationToken.None);

        Assert.Equal(7012, total);
        provider.Verify(p => p.DeleteExpiredAsync(Now.UtcDateTime - CredentialRetentionService.Grace, CredentialRetentionService.BatchSize, It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task Expired_invitations_are_asked_for_until_a_batch_comes_back_short()
    {
        var expiredAt = Now.UtcDateTime.AddHours(-3);
        var full = Enumerable.Range(0, CredentialRetentionService.BatchSize).Select(_ => new ExpiredInvitation(Guid.NewGuid(), 4, "a@example.com", expiredAt)).ToList();
        var provider = new Mock<ICredentialRetentionProvider>();
        provider.SetupSequence(p => p.AnnounceExpiredInvitationsAsync(Now.UtcDateTime, CredentialRetentionService.BatchSize, It.IsAny<CancellationToken>()))
            .ReturnsAsync(full)
            .ReturnsAsync([new ExpiredInvitation(Guid.NewGuid(), 4, "b@example.com", expiredAt)]);
        provider.Setup(p => p.GetUnconfirmedAccountsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var services = new ServiceCollection().AddSingleton(provider.Object).AddSingleton(Mock.Of<IAccountDeletionService>()).BuildServiceProvider();
        var service = new CredentialRetentionService(
            services.GetRequiredService<IServiceScopeFactory>(), new FixedTimeProvider(Now), NullLogger<CredentialRetentionService>.Instance);

        await service.SweepAsync(CancellationToken.None);

        provider.Verify(p => p.AnnounceExpiredInvitationsAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Accounts_never_confirmed_for_30_days_are_deleted_page_by_page_past_one_that_is_skipped_or_fails()
    {
        var createdBefore = Now.UtcDateTime - TimeSpan.FromDays(30);
        var firstPage = Enumerable.Range(1, CredentialRetentionService.UnconfirmedBatchSize).ToList();
        var provider = new Mock<ICredentialRetentionProvider>();
        provider.Setup(p => p.AnnounceExpiredInvitationsAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        provider.Setup(p => p.GetUnconfirmedAccountsAsync(createdBefore, Now.UtcDateTime, 0, CredentialRetentionService.UnconfirmedBatchSize, It.IsAny<CancellationToken>()))
            .ReturnsAsync(firstPage);
        provider.Setup(p => p.GetUnconfirmedAccountsAsync(createdBefore, Now.UtcDateTime, CredentialRetentionService.UnconfirmedBatchSize, CredentialRetentionService.UnconfirmedBatchSize, It.IsAny<CancellationToken>()))
            .ReturnsAsync([500]);
        var deletion = new Mock<IAccountDeletionService>();
        deletion.Setup(d => d.DeleteUnconfirmedAsync(It.IsAny<int>(), createdBefore, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        // Confirmed between the read and the delete: skipped, and the pages after it still go.
        deletion.Setup(d => d.DeleteUnconfirmedAsync(3, createdBefore, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        // A fault on one account is logged, and the rest still go.
        deletion.Setup(d => d.DeleteUnconfirmedAsync(4, createdBefore, It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("deadlock"));
        var services = new ServiceCollection().AddSingleton(provider.Object).AddSingleton(deletion.Object).BuildServiceProvider();
        var service = new CredentialRetentionService(
            services.GetRequiredService<IServiceScopeFactory>(), new FixedTimeProvider(Now), NullLogger<CredentialRetentionService>.Instance);

        await service.SweepAsync(CancellationToken.None);

        deletion.Verify(d => d.DeleteUnconfirmedAsync(It.IsAny<int>(), createdBefore, It.IsAny<CancellationToken>()), Times.Exactly(CredentialRetentionService.UnconfirmedBatchSize + 1));
        deletion.Verify(d => d.DeleteUnconfirmedAsync(500, createdBefore, It.IsAny<CancellationToken>()), Times.Once);
        provider.Verify(p => p.GetUnconfirmedAccountsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
