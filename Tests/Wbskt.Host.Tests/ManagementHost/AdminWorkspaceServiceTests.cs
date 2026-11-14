using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.Auth.Models;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.StaffActions;
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
    private readonly Mock<IWorkspaceCreditCapService> _caps = new();
    private readonly RecordingRunner _runner = new();

    private AdminWorkspaceService Service() => new(_workspaces.Object, _caps.Object, _runner, NullLogger<AdminWorkspaceService>.Instance);

    private void WorkspaceExists(int workspaceId) =>
        _workspaces.Setup(w => w.FindAsync(WorkspaceRef, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkspaceAccountResponse(workspaceId, 3, Guid.NewGuid(), PlanCode.Pro, null, 1));

    [Fact]
    public async Task A_cap_is_set_through_the_consoles_method_as_a_staff_action_on_the_workspace()
    {
        WorkspaceExists(7);
        _caps.Setup(c => c.SetAsync(7, new WorkspaceCreditCapRequest(500m), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkspaceCreditCapResponse(500m, 120m, false));

        var result = await Service().SetCreditCapAsync(WorkspaceRef, new AdminSetWorkspaceCreditCapRequest(Staff, 500m), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new StaffAction(AdminWorkspaceService.SetCreditCapAction, AdminWorkspaceService.TargetKind, WorkspaceRef), _runner.Action);
        Assert.Equal(Staff, _runner.Staff);
    }

    [Fact]
    public async Task A_refused_cap_is_refused_through_the_runner()
    {
        WorkspaceExists(7);
        _caps.Setup(c => c.SetAsync(7, It.IsAny<WorkspaceCreditCapRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Error.Unavailable("WORKSPACE_CREDIT_CAP_BUSY", "Busy."));

        var result = await Service().SetCreditCapAsync(WorkspaceRef, new AdminSetWorkspaceCreditCapRequest(Staff, null), CancellationToken.None);

        Assert.Equal("WORKSPACE_CREDIT_CAP_BUSY", result.Error.Code);
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

    private sealed class RecordingRunner : IStaffActionRunner
    {
        public StaffChange? Staff { get; private set; }

        public StaffAction? Action { get; private set; }

        public async Task<Result<StaffActionResponse>> RunAsync(StaffChange staff, StaffAction action, Func<CancellationToken, Task<Result>> change, CancellationToken cancellationToken)
        {
            Staff = staff;
            Action = action;
            var outcome = await change(cancellationToken);
            return outcome.IsFailure ? outcome.Error : new StaffActionResponse(1, false);
        }
    }
}
