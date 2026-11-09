using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Billing;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Providers;
using Wbskt.Management.Host.Services.Billing;

namespace Wbskt.Host.Tests.ManagementHost;

public sealed class CreditGrantExpiryWarningTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<ICreditGrantProvider> _grants = new();
    private readonly Mock<IEventBus> _bus = new();

    [Fact]
    public async Task Each_grant_claimed_is_announced_with_what_it_has_left_and_one_not_published_is_given_back()
    {
        var expires = Now.UtcDateTime.AddDays(12);
        _grants.Setup(g => g.ClaimExpiringAsync(Now.UtcDateTime, Now.UtcDateTime.AddDays(30), Now.UtcDateTime, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ExpiringCreditGrant(7, "dodo", "pay_1", 60m, expires, [31, 32]), new ExpiringCreditGrant(8, "dodo", "pay_2", 5m, expires, [])]);
        var published = new List<TenantCreditGrantExpiringEvent>();
        _bus.Setup(b => b.PublishAsync(It.IsAny<TenantCreditGrantExpiringEvent>(), It.IsAny<CancellationToken>()))
            .Returns((TenantCreditGrantExpiringEvent e, CancellationToken _) => e.SourceRef == "pay_2" ? Task.FromException(new InvalidOperationException("broker down")) : Task.CompletedTask)
            .Callback((TenantCreditGrantExpiringEvent e, CancellationToken _) => published.Add(e));

        await new CreditGrantExpiryWarning(_grants.Object, _bus.Object, new FixedClock(), NullLogger<CreditGrantExpiryWarning>.Instance).WarnAsync(CancellationToken.None);

        var first = published.Single(e => e.SourceRef == "pay_1");
        Assert.Equal((7, 60m, new DateTimeOffset(expires, TimeSpan.Zero)), (first.TenantId, first.Remaining, first.ExpiresAt));
        Assert.Equal([31, 32], first.WorkspaceIds);
        _grants.Verify(g => g.ReleaseExpiryWarningAsync("dodo", "pay_2", It.IsAny<CancellationToken>()), Times.Once);
        _grants.Verify(g => g.ReleaseExpiryWarningAsync("dodo", "pay_1", It.IsAny<CancellationToken>()), Times.Never);
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
