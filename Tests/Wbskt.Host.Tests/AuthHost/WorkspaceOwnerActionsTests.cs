using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Host.Providers;
using Wbskt.Auth.Host.Services;
using Wbskt.Auth.Host.Telemetry;
using Wbskt.EventBus.Abstractions;
using Wbskt.Infrastructure.Events.Outbox;
using Wbskt.Primitives.Constants;

namespace Wbskt.Host.Tests.AuthHost;

/// <summary>
/// Handing a workspace to someone and deleting it ask for more than the users.manage their endpoints
/// declare (#947). The transfer grants the new owner Admin in the workspace, so it needs the owner or
/// roles.manage tenant-wide; the delete retires everything in it, so it needs the owner or
/// workspaces.delete tenant-wide. Otherwise users.manage in one workspace makes its holder that
/// workspace's administrator, or lets them delete it.
/// </summary>
public sealed class WorkspaceOwnerActionsTests
{
    private const int WorkspaceId = 20;
    private const int TenantId = 5;
    private const int OwnerId = 1;
    private const int ManagerId = 2;
    private static readonly Guid ManagerRef = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid OwnerRef = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly Mock<IWorkspaceProvider> _workspaces = new();
    private readonly Mock<IAccountProvider> _accounts = new();
    private readonly Mock<IAccessControlProvider> _access = new();

    public WorkspaceOwnerActionsTests()
    {
        _workspaces.Setup(p => p.FindByIdAsync(WorkspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Workspace { Id = WorkspaceId, OwnerUserId = OwnerId, TenantId = TenantId, Name = "W" });
        _accounts.Setup(p => p.FindIdByRefIdAsync(ManagerRef, It.IsAny<CancellationToken>())).ReturnsAsync(ManagerId);
        _accounts.Setup(p => p.GetByIdAsync(OwnerId, It.IsAny<CancellationToken>())).ReturnsAsync(new User { Id = OwnerId, RefId = OwnerRef });
    }

    [Fact]
    public async Task A_member_with_only_users_manage_cannot_make_themselves_owner()
    {
        var result = await Service().TransferOwnershipAsync(ManagerId, WorkspaceId, ManagerRef);

        Assert.True(result.IsFailure);
        Assert.Equal("PERMISSION_UNAUTHORIZED", result.Error.Code);
        _workspaces.Verify(p => p.SetOwnerAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Roles_manage_tenant_wide_may_transfer_a_workspace_it_does_not_own()
    {
        _access.Setup(p => p.VerifyPermissionAsync(ManagerId, TenantId, null, PermissionNames.RolesManage, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await Service().TransferOwnershipAsync(ManagerId, WorkspaceId, ManagerRef);

        Assert.True(result.IsSuccess);
        _workspaces.Verify(p => p.SetOwnerAsync(WorkspaceId, ManagerId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task The_owner_may_hand_their_workspace_on()
    {
        var result = await Service().TransferOwnershipAsync(OwnerId, WorkspaceId, ManagerRef);

        Assert.True(result.IsSuccess);
        _workspaces.Verify(p => p.SetOwnerAsync(WorkspaceId, ManagerId, It.IsAny<CancellationToken>()), Times.Once);
        _access.Verify(p => p.VerifyPermissionAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_member_with_only_users_manage_cannot_delete_the_workspace()
    {
        var result = await Service().DeleteWorkspaceAsync(ManagerId, WorkspaceId);

        Assert.True(result.IsFailure);
        Assert.Equal("PERMISSION_UNAUTHORIZED", result.Error.Code);
        _workspaces.Verify(p => p.DeleteWorkspaceAsync(It.IsAny<int>(), It.IsAny<OutboxMessage>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Workspaces_delete_tenant_wide_may_delete_a_workspace_it_does_not_own()
    {
        _access.Setup(p => p.VerifyPermissionAsync(ManagerId, TenantId, null, PermissionNames.WorkspacesDelete, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await Service().DeleteWorkspaceAsync(ManagerId, WorkspaceId);

        Assert.True(result.IsSuccess);
        _workspaces.Verify(p => p.DeleteWorkspaceAsync(WorkspaceId, It.IsAny<OutboxMessage>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task The_owner_may_delete_their_workspace()
    {
        var result = await Service().DeleteWorkspaceAsync(OwnerId, WorkspaceId);

        Assert.True(result.IsSuccess);
        _workspaces.Verify(p => p.DeleteWorkspaceAsync(WorkspaceId, It.IsAny<OutboxMessage>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private WorkspaceService Service() => new(
        _workspaces.Object, _accounts.Object, Mock.Of<ITenantProvider>(), _access.Object,
        Mock.Of<IEventBus>(), NullLogger<WorkspaceService>.Instance, new AuthMetrics(), new FixedTenantLimits(), TimeProvider.System);
}
