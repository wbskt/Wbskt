using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.Auth.Models;
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
    public async Task Every_page_is_read_and_only_writes_that_changed_something_count_as_repairs()
    {
        CreditPurchaseResponse kept = Purchase(1, "pay_kept");
        CreditPurchaseResponse lost = Purchase(2, "pay_lost");
        CreditPurchaseResponse refunded = Purchase(3, "pay_refunded") with { RefundedAt = Bought.AddDays(2) };
        CreditPurchaseResponse held = Purchase(4, "pay_held") with { Held = true, HoldRevision = 1 };
        _auth.Setup(x => x.GetPageAsync(null, It.IsAny<CancellationToken>())).ReturnsAsync(new Page<CreditPurchaseResponse> { Items = [kept, lost], NextCursor = "2" });
        _auth.Setup(x => x.GetPageAsync("2", It.IsAny<CancellationToken>())).ReturnsAsync(new Page<CreditPurchaseResponse> { Items = [refunded, held], NextCursor = null });
        _grants.Setup(x => x.InsertAsync(It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), "dodo", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int _, decimal _, DateTime _, DateTime _, string _, string reference, CancellationToken _) => reference == "pay_lost");
        _grants.Setup(x => x.RevokeAsync("dodo", "pay_refunded", Bought.AddDays(2).UtcDateTime, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _grants.Setup(x => x.SetHoldAsync("dodo", "pay_held", true, 1, It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        int repaired = await Reconcile().ReconcileAsync(CancellationToken.None);

        Assert.Equal(3, repaired);
        _grants.Verify(x => x.InsertAsync(7, 500m, Bought.UtcDateTime, Bought.AddDays(365).UtcDateTime, "dodo", "pay_lost", It.IsAny<CancellationToken>()));
        _grants.Verify(x => x.RevokeAsync("dodo", "pay_kept", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
        _grants.Verify(x => x.SetHoldAsync("dodo", "pay_kept", It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never, "a pack never held has no hold to repair");
    }

    [Fact]
    public async Task An_auth_host_that_cannot_answer_fails_the_tick()
    {
        _auth.Setup(x => x.GetPageAsync(null, It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("down"));

        await Assert.ThrowsAsync<HttpRequestException>(() => Reconcile().ReconcileAsync(CancellationToken.None));
        _grants.VerifyNoOtherCalls();
    }

    private static CreditPurchaseResponse Purchase(int id, string paymentId) =>
        new(id, 7, Guid.Empty, "dodo", paymentId, 500m, Bought, Bought.AddDays(365), null, false, 0);

    private CreditGrantReconcile Reconcile() =>
        new(_auth.Object, _grants.Object, new ManagementMetrics(), TimeProvider.System, NullLogger<CreditGrantReconcile>.Instance);
}
