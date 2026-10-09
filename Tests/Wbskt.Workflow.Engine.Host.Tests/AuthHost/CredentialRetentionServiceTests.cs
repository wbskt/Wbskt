using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.Auth.Host.Providers;
using Wbskt.Auth.Host.Services;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Auth;

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
        provider.Setup(p => p.AnnounceExpiredInvitationsAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var services = new ServiceCollection().AddSingleton(provider.Object).AddSingleton(Mock.Of<IEventBus>()).BuildServiceProvider();
        var service = new CredentialRetentionService(
            services.GetRequiredService<IServiceScopeFactory>(), new FixedTimeProvider(Now), NullLogger<CredentialRetentionService>.Instance);

        var total = await service.SweepAsync(CancellationToken.None);

        Assert.Equal(7012, total);
        provider.Verify(p => p.DeleteExpiredAsync(Now.UtcDateTime - CredentialRetentionService.Grace, CredentialRetentionService.BatchSize, It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task Each_invitation_that_ran_out_is_logged_once_at_the_moment_it_expired_in_its_tenants_workspaces()
    {
        var expiredAt = Now.UtcDateTime.AddHours(-3);
        var first = new ExpiredInvitation(Guid.NewGuid(), 4, "amal@example.com", expiredAt);
        var second = new ExpiredInvitation(Guid.NewGuid(), 4, "pika@example.com", expiredAt);
        var provider = new Mock<ICredentialRetentionProvider>();
        provider.SetupSequence(p => p.AnnounceExpiredInvitationsAsync(Now.UtcDateTime, CredentialRetentionService.BatchSize, It.IsAny<CancellationToken>()))
            .ReturnsAsync([first, second]);
        var audit = new Mock<IAuditWorkspaces>();
        audit.Setup(a => a.OfTenantAsync(4, It.IsAny<CancellationToken>())).ReturnsAsync([10, 11]);
        var published = new List<InvitationExpiredEvent>();
        var bus = new Mock<IEventBus>();
        bus.Setup(b => b.PublishAsync(It.IsAny<InvitationExpiredEvent>(), It.IsAny<CancellationToken>()))
            .Callback<InvitationExpiredEvent, CancellationToken>((e, _) => published.Add(e))
            .Returns(Task.CompletedTask);
        var services = new ServiceCollection().AddSingleton(provider.Object).AddSingleton(bus.Object).AddSingleton(audit.Object).BuildServiceProvider();
        var service = new CredentialRetentionService(
            services.GetRequiredService<IServiceScopeFactory>(), new FixedTimeProvider(Now), NullLogger<CredentialRetentionService>.Instance);

        await service.SweepAsync(CancellationToken.None);

        Assert.Equal(["amal@example.com", "pika@example.com"], published.Select(e => e.Email));
        Assert.All(published, e => Assert.Equal([10, 11], e.WorkspaceIds));
        Assert.All(published, e => Assert.Equal(expiredAt, e.CreatedAtUtc));
        audit.Verify(a => a.OfTenantAsync(4, It.IsAny<CancellationToken>()), Times.Once);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
