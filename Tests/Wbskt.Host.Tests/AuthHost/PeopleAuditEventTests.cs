using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.Auth.Models;
using Wbskt.Auth.Host.Providers;
using Wbskt.Auth.Host.Services;
using Wbskt.Auth.Host.Services.Email;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Events.Auth;
using Wbskt.Infrastructure.Security;

namespace Wbskt.Host.Tests.AuthHost;

/// <summary>
/// Changes to a tenant's people reach the audit log of every workspace in the tenant, since the log
/// is read one workspace at a time.
/// </summary>
public sealed class PeopleAuditEventTests
{
    private const int CallerId = 1;
    private const int TenantId = 5;
    private static readonly IReadOnlyList<int> TenantWorkspaces = [10, 11];

    private readonly Mock<ITenantProvider> _tenants = new();
    private readonly Mock<IAccessControlProvider> _accessControl = new();
    private readonly Mock<IAuditWorkspaces> _audit = new();
    private readonly List<IEvent> _published = [];
    private readonly TenantService _tenantService;
    private readonly AccessControlService _accessControlService;

    public PeopleAuditEventTests()
    {
        _audit.Setup(a => a.OfTenantAsync(TenantId, It.IsAny<CancellationToken>())).ReturnsAsync(TenantWorkspaces);
        var bus = new Mock<IEventBus>();
        bus.Setup(b => b.PublishAsync(It.IsAny<IEvent>(), It.IsAny<CancellationToken>()))
            .Callback<IEvent, CancellationToken>((e, _) => _published.Add(e))
            .Returns(Task.CompletedTask);
        bus.Setup(b => b.PublishAsync(It.IsAny<TenantActorEvent>(), It.IsAny<CancellationToken>()))
            .Callback<IEvent, CancellationToken>((e, _) => _published.Add(e))
            .Returns(Task.CompletedTask);
        bus.Setup(b => b.PublishAsync(It.IsAny<RolePermissionsChangedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<IEvent, CancellationToken>((e, _) => _published.Add(e))
            .Returns(Task.CompletedTask);

        _tenantService = new TenantService(
            _tenants.Object, _accessControl.Object, Mock.Of<IAccountProvider>(), Mock.Of<IWorkspaceProvider>(), bus.Object, Mock.Of<IAuthMailer>(),
            NullLogger<TenantService>.Instance, _audit.Object);
        _accessControlService = new AccessControlService(
            _accessControl.Object, _tenants.Object, Mock.Of<IAccountProvider>(), Mock.Of<IWorkspaceProvider>(), bus.Object,
            NullLogger<AccessControlService>.Instance, _audit.Object);
    }

    [Fact]
    public async Task An_invitation_is_logged_in_every_workspace_of_the_tenant()
    {
        var invitationRef = Guid.NewGuid();
        _tenants.Setup(p => p.CreateInvitationAsync(TenantId, "amal@example.test", null, It.IsAny<byte[]>(), It.IsAny<DateTime>(), CallerId, It.IsAny<IReadOnlyCollection<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreatedInvitation(invitationRef, "Acme"));

        await _tenantService.CreateInvitationAsync(CallerId, TenantId, new CreateInvitationRequest("amal@example.test", null), CancellationToken.None);

        var sent = Assert.IsType<InvitationSentEvent>(Assert.Single(_published));
        Assert.Equal((invitationRef, "amal@example.test"), (sent.InvitationRefId, sent.Email));
        Assert.Equal(TenantWorkspaces, sent.WorkspaceIds);
    }

    [Fact]
    public async Task Revoking_is_logged_with_the_address_only_when_something_was_revoked()
    {
        var outstanding = Guid.NewGuid();
        var spent = Guid.NewGuid();
        _tenants.Setup(p => p.RevokeInvitationAsync(outstanding, TenantId, It.IsAny<CancellationToken>())).ReturnsAsync("amal@example.test");
        _tenants.Setup(p => p.RevokeInvitationAsync(spent, TenantId, It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);

        await _tenantService.RevokeInvitationAsync(TenantId, outstanding, CancellationToken.None);
        await _tenantService.RevokeInvitationAsync(TenantId, spent, CancellationToken.None);

        var revoked = Assert.IsType<InvitationRevokedEvent>(Assert.Single(_published));
        Assert.Equal((outstanding, "amal@example.test"), (revoked.InvitationRefId, revoked.Email));
    }

    [Fact]
    public async Task A_role_permission_change_is_logged_with_the_permissions_before_and_after()
    {
        var roleRef = Guid.NewGuid();
        _accessControl.Setup(p => p.FindRoleIdByRefIdAsync(roleRef, TenantId, It.IsAny<CancellationToken>())).ReturnsAsync(42);
        _accessControl.SetupSequence(p => p.GetRolePermissionsAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new RolePermissionAssignmentResponse("devices.read", false)])
            .ReturnsAsync([new RolePermissionAssignmentResponse("users.manage", true), new RolePermissionAssignmentResponse("devices.read", false)]);

        await _accessControlService.GrantRolePermissionAsync(TenantId, roleRef, new GrantRolePermissionRequest("users.manage", true), CancellationToken.None);

        // The permission caches hear of it as before; the audit log gets the before and after.
        Assert.Contains(_published, e => e is RolePermissionsChangedEvent { RoleId: 42 });
        var updated = Assert.Single(_published.OfType<RolePermissionsUpdatedEvent>());
        Assert.Equal(roleRef, updated.RoleRefId);
        Assert.Equal([new FieldChange("permissions", "devices.read", "!users.manage,devices.read")], updated.Changes);
        Assert.Equal(TenantWorkspaces, updated.WorkspaceIds);
    }

    [Fact]
    public async Task A_workspace_lookup_that_fails_logs_in_no_workspace_rather_than_failing()
    {
        var workspaces = new Mock<IWorkspaceProvider>();
        workspaces.Setup(w => w.GetIdsByTenantAsync(TenantId, It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("database down"));

        var ids = await new AuditWorkspaces(workspaces.Object, NullLogger<AuditWorkspaces>.Instance).OfTenantAsync(TenantId, CancellationToken.None);

        Assert.Empty(ids);
    }
}
