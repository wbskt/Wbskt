using Moq;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Billing;
using Wbskt.Infrastructure;
using Wbskt.Management.Host.Providers;
using Wbskt.Management.Host.Services.Billing;
using Wbskt.Management.Models;

namespace Wbskt.Host.Tests.ManagementHost;

public sealed class WorkspaceCreditCapServiceTests
{
    private readonly Mock<IWorkspaceCreditCapProvider> _caps = new();
    private readonly Mock<IEventBus> _eventBus = new();
    private readonly FixedClock _clock = new();

    [Fact]
    public async Task Setting_a_cap_stores_it_for_the_current_month_and_announces_it()
    {
        _caps.Setup(x => x.SetAsync(7, 500m, new DateOnly(2026, 11, 1), It.IsAny<CancellationToken>())).ReturnsAsync(120m);

        Result<WorkspaceCreditCapResponse> result = await Service().SetAsync(7, new WorkspaceCreditCapRequest(500m), CancellationToken.None);

        Assert.Equal(new WorkspaceCreditCapResponse(500m, 120m, false), result.Value);
        _eventBus.Verify(x => x.PublishAsync(It.Is<WorkspaceCreditCapSetEvent>(e => e.WorkspaceId == 7 && e.MonthlyCreditCap == 500m), It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task A_cap_lowered_below_what_was_spent_says_runs_are_refused_now()
    {
        _caps.Setup(x => x.SetAsync(7, 100m, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>())).ReturnsAsync(120m);

        Result<WorkspaceCreditCapResponse> result = await Service().SetAsync(7, new WorkspaceCreditCapRequest(100m), CancellationToken.None);

        Assert.True(result.Value.CapReached);
    }

    [Fact]
    public async Task Removing_the_cap_is_never_reached()
    {
        _caps.Setup(x => x.SetAsync(7, null, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>())).ReturnsAsync(120m);

        Result<WorkspaceCreditCapResponse> result = await Service().SetAsync(7, new WorkspaceCreditCapRequest(null), CancellationToken.None);

        Assert.Equal(new WorkspaceCreditCapResponse(null, 120m, false), result.Value);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2_000_000_000_000)]
    public async Task A_cap_out_of_range_is_refused_and_stores_nothing(double cap)
    {
        Result<WorkspaceCreditCapResponse> result = await Service().SetAsync(7, new WorkspaceCreditCapRequest((decimal)cap), CancellationToken.None);

        Assert.Equal("WORKSPACE_CREDIT_CAP_INVALID", result.Error.Code);
        _caps.VerifyNoOtherCalls();
        _eventBus.VerifyNoOtherCalls();
    }

    private WorkspaceCreditCapService Service() => new(_caps.Object, _eventBus.Object, _clock);

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 9, 4, 0, 0, TimeSpan.Zero);
    }
}
