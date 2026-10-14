using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Host.Providers;
using Wbskt.Auth.Host.Services;
using Wbskt.Auth.Models;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Auth;
using Wbskt.Infrastructure;
using Wbskt.Models;

namespace Wbskt.Host.Tests.AuthHost;

/// <summary>
/// The access-control aggregate: roles, groups and the grants and assignments between them. Every
/// reference is resolved within the tenant before anything is written, and every change that can
/// alter someone's effective permissions tells the caches that hold them.
/// </summary>
public sealed class AccessControlServiceTests
{
    private const int TenantId = 5;
    private const int MemberId = 7;
    private const int RoleId = 3;
    private const int GroupId = 4;
    private const int WorkspaceId = 11;
    private static readonly Guid MemberRef = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly Guid RoleRef = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid GroupRef = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid WorkspaceRef = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private readonly Mock<IAccessControlProvider> _accessControl = new();
    private readonly Mock<ITenantProvider> _tenants = new();
    private readonly Mock<IAccountProvider> _accounts = new();
    private readonly Mock<IWorkspaceProvider> _workspaces = new();
    private readonly RecordingBus _bus = new();
    private readonly AccessControlService _service;

    public AccessControlServiceTests()
    {
        _tenants.Setup(p => p.FindUserIdByRefIdInTenantAsync(MemberRef, TenantId, It.IsAny<CancellationToken>())).ReturnsAsync(MemberId);
        _accessControl.Setup(p => p.FindRoleIdByRefIdAsync(RoleRef, TenantId, It.IsAny<CancellationToken>())).ReturnsAsync(RoleId);
        _accessControl.Setup(p => p.FindGroupIdByRefIdAsync(GroupRef, TenantId, It.IsAny<CancellationToken>())).ReturnsAsync(GroupId);
        _workspaces.Setup(p => p.FindIdByRefIdAsync(WorkspaceRef, It.IsAny<CancellationToken>())).ReturnsAsync(WorkspaceId);
        _accounts.Setup(p => p.GetByIdAsync(MemberId, It.IsAny<CancellationToken>())).ReturnsAsync(new User { Id = MemberId, RefId = MemberRef });

        _service = new AccessControlService(
            _accessControl.Object, _tenants.Object, _accounts.Object, _workspaces.Object, _bus,
            NullLogger<AccessControlService>.Instance);
    }

    // ---------------------------------------------------------------- roles and groups

    [Fact]
    public async Task A_role_name_already_taken_is_a_conflict()
    {
        _accessControl.Setup(p => p.InsertRoleAsync("Editors", null, TenantId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(SqlExceptions.WithNumber(2627, "Violation of UNIQUE KEY constraint."));

        var result = await _service.CreateRoleAsync(TenantId, new CreateRoleRequest("Editors", null));

        Assert.Equal("AUTH_ROLE_CONFLICT", result.Error.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
    }

    [Fact]
    public async Task A_role_outside_the_tenant_is_not_updated()
    {
        var result = await _service.UpdateRoleAsync(TenantId, Guid.NewGuid(), new UpdateRoleRequest("Editors", null));

        Assert.Equal("ROLE_NOT_FOUND", result.Error.Code);
        Assert.Equal(ErrorType.Forbidden, result.Error.Type);
        _accessControl.Verify(p => p.UpdateRoleAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Deleting_a_role_tells_the_permission_caches()
    {
        var result = await _service.DeleteRoleAsync(TenantId, RoleRef);

        Assert.True(result.IsSuccess);
        _accessControl.Verify(p => p.DeleteRoleAsync(RoleId, TenantId, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(RoleId, Assert.IsType<RolePermissionsChangedEvent>(Assert.Single(_bus.Published)).RoleId);
    }

    [Fact]
    public async Task A_group_under_an_unknown_parent_is_not_created()
    {
        var result = await _service.CreateGroupAsync(TenantId, new CreateGroupRequest("Editors", Guid.NewGuid()));

        Assert.Equal("GROUP_NOT_FOUND", result.Error.Code);
        _accessControl.Verify(p => p.InsertGroupAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task The_permission_catalogue_is_global()
    {
        IPagedList<PermissionResponse> catalogue = new PagedList<PermissionResponse>([new PermissionResponse("workspaces.read", null)], 1);
        _accessControl.Setup(p => p.GetPermissionsAsync(0, 10, It.IsAny<CancellationToken>())).ReturnsAsync(catalogue);

        var result = await _service.GetPermissionsAsync(TenantId, 0, 10);

        Assert.Same(catalogue, result.Value);
    }

    // ---------------------------------------------------------------- assignments

    [Fact]
    public async Task A_role_is_not_assigned_to_someone_outside_the_tenant()
    {
        var result = await _service.AssignUserRoleAsync(TenantId, Guid.NewGuid(), RoleRef, null);

        Assert.Equal("USER_NOT_FOUND", result.Error.Code);
        _accessControl.Verify(p => p.AssignUserRoleAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Empty(_bus.Published);
    }

    [Fact]
    public async Task Assigning_a_role_in_a_workspace_is_scoped_to_it_and_announced()
    {
        var result = await _service.AssignUserRoleAsync(TenantId, MemberRef, RoleRef, WorkspaceRef);

        Assert.True(result.IsSuccess);
        _accessControl.Verify(p => p.AssignUserRoleAsync(MemberId, RoleId, TenantId, WorkspaceId, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Collection(_bus.Published,
            e => Assert.Equal(MemberId, Assert.IsType<UserPermissionsChangedEvent>(e).UserId),
            e =>
            {
                var assigned = Assert.IsType<MemberRoleAssignedEvent>(e);
                Assert.Equal((MemberRef, RoleRef, (Guid?)WorkspaceRef), (assigned.MemberRefId, assigned.RoleRefId, assigned.WorkspaceRefId));
            });
    }

    [Fact]
    public async Task An_unknown_workspace_scope_writes_nothing()
    {
        var granted = await _service.GrantUserPermissionAsync(TenantId, MemberRef, new GrantPermissionRequest("workspaces.read", WorkspaceRef: Guid.NewGuid()));
        var assigned = await _service.AssignGroupRoleAsync(TenantId, GroupRef, RoleRef, Guid.NewGuid());

        foreach (var result in new[] { granted, assigned })
        {
            Assert.Equal("WORKSPACE_NOT_FOUND", result.Error.Code);
        }

        _accessControl.Verify(p => p.GrantUserPermissionAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
        _accessControl.Verify(p => p.AssignGroupRoleAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Empty(_bus.Published);
    }

    [Fact]
    public async Task A_group_role_change_reaches_everyone_holding_the_role()
    {
        var result = await _service.AssignGroupRoleAsync(TenantId, GroupRef, RoleRef, null);

        Assert.True(result.IsSuccess);
        _accessControl.Verify(p => p.AssignGroupRoleAsync(GroupId, RoleId, TenantId, null, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(RoleId, Assert.IsType<RolePermissionsChangedEvent>(Assert.Single(_bus.Published)).RoleId);
    }

    // ---------------------------------------------------------------- role permissions

    [Fact]
    public async Task A_grant_that_changes_the_role_is_logged_with_before_and_after()
    {
        IReadOnlyCollection<RolePermissionAssignmentResponse> before = [];
        IReadOnlyCollection<RolePermissionAssignmentResponse> after = [new RolePermissionAssignmentResponse("workspaces.read", false)];
        _accessControl.SetupSequence(p => p.GetRolePermissionsAsync(RoleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(before)
            .ReturnsAsync(after);

        var result = await _service.GrantRolePermissionAsync(TenantId, RoleRef, new GrantRolePermissionRequest("workspaces.read"));

        Assert.True(result.IsSuccess);
        _accessControl.Verify(p => p.GrantRolePermissionAsync(RoleId, "workspaces.read", false, TenantId, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Single(_bus.Published.OfType<RolePermissionsChangedEvent>());
        var updated = Assert.Single(_bus.Published.OfType<RolePermissionsUpdatedEvent>());
        Assert.Equal(RoleRef, updated.RoleRefId);
    }

    [Fact]
    public async Task A_grant_the_role_already_had_is_not_logged_as_a_change()
    {
        IReadOnlyCollection<RolePermissionAssignmentResponse> held = [new RolePermissionAssignmentResponse("workspaces.read", false)];
        _accessControl.Setup(p => p.GetRolePermissionsAsync(RoleId, It.IsAny<CancellationToken>())).ReturnsAsync(held);

        var result = await _service.GrantRolePermissionAsync(TenantId, RoleRef, new GrantRolePermissionRequest("workspaces.read"));

        Assert.True(result.IsSuccess);
        // The caches are still told: they cannot know the grant was a no-op.
        Assert.Single(_bus.Published.OfType<RolePermissionsChangedEvent>());
        Assert.Empty(_bus.Published.OfType<RolePermissionsUpdatedEvent>());
    }

    private sealed class RecordingBus : IEventBus
    {
        public List<IEvent> Published { get; } = [];

        public Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default) where TEvent : IEvent
        {
            Published.Add(@event);
            return Task.CompletedTask;
        }
    }
}
