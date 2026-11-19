using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.Auth.Models;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Billing;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.StaffActions;
using Wbskt.Management.Host.Providers;
using Wbskt.Management.Host.Services.Admin;
using Wbskt.Management.Host.Services.Billing;
using Wbskt.Management.Host.Services.Clients;
using Wbskt.Management.Models;
using Wbskt.Models.Admin;
using Wbskt.Models.Billing;

namespace Wbskt.Host.Tests.ManagementHost;

public sealed class AdminWorkspaceServiceTests
{
    private static readonly Guid WorkspaceRef = Guid.NewGuid();
    private static readonly StaffChange Staff = new(Guid.NewGuid(), "pika", "Runaway workflow on a trial", Guid.NewGuid());

    private readonly Mock<IWorkspaceAccountsClient> _workspaces = new();
    private readonly Mock<IWorkspaceCreditCapProvider> _caps = new();
    private readonly RecordingBus _bus = new();
    private readonly RecordingStaffActionRunner _runner = new();

    private AdminWorkspaceService Service() =>
        new(_workspaces.Object, _caps.Object, _bus, TimeProvider.System, _runner, NullLogger<AdminWorkspaceService>.Instance);

    private void WorkspaceExists(int workspaceId) =>
        _workspaces.Setup(w => w.FindAsync(WorkspaceRef, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkspaceAccountResponse(workspaceId, 3, Guid.NewGuid(), PlanCode.Pro, null, 1));

    [Fact]
    public async Task A_cap_is_set_through_the_consoles_service_as_a_staff_action_on_the_workspace()
    {
        WorkspaceExists(7);
        _caps.Setup(c => c.SetAsync(7, 500m, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>())).ReturnsAsync(120m);
        _runner.Inspect = () => Assert.Empty(_bus.Published);

        var result = await Service().SetCreditCapAsync(WorkspaceRef, new AdminSetWorkspaceCreditCapRequest(Staff, 500m), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new StaffAction(AdminWorkspaceService.SetCreditCapAction, AdminWorkspaceService.TargetKind, WorkspaceRef), _runner.Action);
        Assert.Equal(Staff, _runner.Staff);
        var sent = Assert.IsType<WorkspaceCreditCapSetEvent>(Assert.Single(_bus.Published));
        Assert.Equal((7, (decimal?)500m), (sent.WorkspaceId, sent.MonthlyCreditCap));
    }

    [Fact]
    public async Task A_broker_that_refuses_the_event_after_the_cap_is_set_does_not_fail_the_change()
    {
        WorkspaceExists(7);
        _caps.Setup(c => c.SetAsync(7, 500m, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>())).ReturnsAsync(120m);
        _bus.Fails = true;

        var result = await Service().SetCreditCapAsync(WorkspaceRef, new AdminSetWorkspaceCreditCapRequest(Staff, 500m), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(["event"], _runner.FailedAfterCommit);
    }

    [Fact]
    public async Task A_refused_cap_is_refused_through_the_runner_and_sends_nothing()
    {
        WorkspaceExists(7);
        _caps.Setup(c => c.SetAsync(7, It.IsAny<decimal?>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(Wbskt.Host.Tests.AuthHost.SqlExceptions.WithNumber(50035, "busy"));

        var result = await Service().SetCreditCapAsync(WorkspaceRef, new AdminSetWorkspaceCreditCapRequest(Staff, null), CancellationToken.None);

        Assert.Equal("WORKSPACE_CREDIT_CAP_BUSY", result.Error.Code);
        Assert.Empty(_bus.Published);
    }

    [Fact]
    public async Task A_replayed_cap_sends_no_second_event()
    {
        WorkspaceExists(7);
        _runner.Replay = true;

        var result = await Service().SetCreditCapAsync(WorkspaceRef, new AdminSetWorkspaceCreditCapRequest(Staff, 500m), CancellationToken.None);

        Assert.True(result.Value.Replayed);
        Assert.Empty(_bus.Published);
        _caps.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task An_unknown_workspace_is_not_found_and_nothing_runs()
    {
        _workspaces.Setup(w => w.FindAsync(WorkspaceRef, It.IsAny<CancellationToken>())).ReturnsAsync((WorkspaceAccountResponse?)null);

        var result = await Service().SetCreditCapAsync(WorkspaceRef, new AdminSetWorkspaceCreditCapRequest(Staff, 500m), CancellationToken.None);

        Assert.Equal("WORKSPACE_NOT_FOUND", result.Error.Code);
        Assert.Null(_runner.Action);
    }

    [Fact]
    public async Task An_auth_host_that_does_not_answer_is_a_503_and_nothing_runs()
    {
        _workspaces.Setup(w => w.FindAsync(WorkspaceRef, It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("refused"));

        var result = await Service().SetCreditCapAsync(WorkspaceRef, new AdminSetWorkspaceCreditCapRequest(Staff, 500m), CancellationToken.None);

        Assert.Equal("AUTH_HOST_UNAVAILABLE", result.Error.Code);
        Assert.Equal(ErrorType.Unavailable, result.Error.Type);
        Assert.Null(_runner.Action);
        _caps.VerifyNoOtherCalls();
    }

    private sealed class RecordingBus : IEventBus
    {
        public List<IEvent> Published { get; } = [];

        public bool Fails { get; set; }

        public Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default) where TEvent : IEvent
        {
            if (Fails)
            {
                throw new InvalidOperationException("The broker refused the event.");
            }

            Published.Add(@event);
            return Task.CompletedTask;
        }
    }
}
