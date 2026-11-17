using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.Auth.Models;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Providers;
using Wbskt.Management.Host.Services.Billing;
using Wbskt.Management.Host.Services.Clients;
using Wbskt.Management.Host.Telemetry;
using Wbskt.Models;

namespace Wbskt.Host.Tests.ManagementHost;

public sealed class CreditGrantReconcileTests
{
    private static readonly DateTimeOffset Bought = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly Mock<ICreditPurchasesClient> _auth = new();
    private readonly Mock<ICreditGrantProvider> _grants = new();

    [Fact]
    public async Task Every_page_is_compared_with_the_grants_and_only_what_one_is_missing_is_written()
    {
        CreditPurchaseResponse kept = Purchase(1, "pay_kept");
        CreditPurchaseResponse lost = Purchase(2, "pay_lost");
        CreditPurchaseResponse refunded = Purchase(3, "pay_refunded") with { RefundedAt = Bought.AddDays(2) };
        CreditPurchaseResponse held = Purchase(4, "pay_held") with { Held = true, HoldRevision = 1 };
        Page(null, "2", kept, lost);
        Page("2", null, refunded, held);
        Granted(new CreditGrantState("dodo", "pay_kept", false, 0), new CreditGrantState("dodo", "pay_refunded", false, 0), new CreditGrantState("dodo", "pay_held", false, 0));
        _grants.Setup(x => x.InsertAsync(It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), "dodo", "pay_lost", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _grants.Setup(x => x.RevokeAsync("dodo", "pay_refunded", Bought.AddDays(2).UtcDateTime, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _grants.Setup(x => x.SetHoldAsync("dodo", "pay_held", true, 1, It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        int repaired = await Reconcile().ReconcileAsync(CancellationToken.None);

        Assert.Equal(3, repaired);
        _grants.Verify(x => x.InsertAsync(7, 500m, Bought.UtcDateTime, Bought.AddDays(365).UtcDateTime, "dodo", "pay_lost", It.IsAny<CancellationToken>()));
        _grants.Verify(x => x.InsertAsync(It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once, "only the grant the main database lacks is written");
        _grants.Verify(x => x.RevokeAsync("dodo", "pay_kept", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
        _grants.Verify(x => x.SetHoldAsync("dodo", "pay_kept", It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never, "a pack never held has no hold to repair");
    }

    [Fact]
    public async Task A_grant_that_has_everything_its_purchase_has_is_not_written_to()
    {
        CreditPurchaseResponse settled = Purchase(1, "pay_settled") with { RefundedAt = Bought.AddDays(2), Held = false, HoldRevision = 2 };
        Page(null, null, settled);
        Granted(new CreditGrantState("dodo", "pay_settled", true, 2));

        int repaired = await Reconcile().ReconcileAsync(CancellationToken.None);

        Assert.Equal(0, repaired);
        _grants.Verify(x => x.GetBySourcesAsync(It.Is<IReadOnlyCollection<CreditGrantSource>>(s => s.SequenceEqual(new[] { new CreditGrantSource("dodo", "pay_settled") })), It.IsAny<CancellationToken>()));
        _grants.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Only_packs_that_have_not_expired_are_read()
    {
        var now = new DateTimeOffset(2026, 11, 17, 9, 0, 0, TimeSpan.Zero);
        Page(null, null);
        Granted();

        await Reconcile(new FixedClock(now)).ReconcileAsync(CancellationToken.None);

        _auth.Verify(x => x.GetPageAsync(null, now, It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task An_auth_host_that_cannot_answer_fails_the_tick()
    {
        _auth.Setup(x => x.GetPageAsync(null, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("down"));

        await Assert.ThrowsAsync<HttpRequestException>(() => Reconcile().ReconcileAsync(CancellationToken.None));
        _grants.VerifyNoOtherCalls();
    }

    private void Page(string? cursor, string? next, params CreditPurchaseResponse[] items) =>
        _auth.Setup(x => x.GetPageAsync(cursor, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Page<CreditPurchaseResponse> { Items = items, NextCursor = next });

    private void Granted(params CreditGrantState[] states) =>
        _grants.Setup(x => x.GetBySourcesAsync(It.IsAny<IReadOnlyCollection<CreditGrantSource>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<CreditGrantSource> sources, CancellationToken _) =>
                states.Where(s => sources.Contains(new CreditGrantSource(s.Source, s.SourceRef))).ToList());

    private static CreditPurchaseResponse Purchase(int id, string paymentId) =>
        new(id, 7, Guid.Empty, "dodo", paymentId, 500m, Bought, Bought.AddDays(365), null, false, 0);

    private CreditGrantReconcile Reconcile(TimeProvider? clock = null) =>
        new(_auth.Object, _grants.Object, new ManagementMetrics(), clock ?? TimeProvider.System, NullLogger<CreditGrantReconcile>.Instance);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
