using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Host.Providers;
using Wbskt.Auth.Host.Services;
using Wbskt.Auth.Host.Services.Email;
using Wbskt.Auth.Models;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Auth;
using Wbskt.Infrastructure;
using Wbskt.Primitives.Constants;
using Wbskt.Primitives.Exceptions;

namespace Wbskt.Host.Tests.AuthHost;

/// <summary>
/// The tenant aggregate: the caller's tenants, its members and its invitations. A member is named by
/// reference and found only within the tenant, so nothing here reaches a user outside it.
/// </summary>
public sealed class TenantServiceTests
{
    private const int CallerId = 1;
    private const int TenantId = 5;
    private const int MemberId = 7;
    private const string Ip = "10.0.0.1";
    private static readonly Guid MemberRef = Guid.Parse("77777777-7777-7777-7777-777777777777");

    private readonly Mock<ITenantProvider> _tenants = new();
    private readonly Mock<IAccessControlProvider> _accessControl = new();
    private readonly Mock<IAccountProvider> _accounts = new();
    private readonly Mock<IWorkspaceProvider> _workspaces = new();
    private readonly RecordingBus _bus = new();
    private readonly TenantService _service;

    public TenantServiceTests()
    {
        _tenants.Setup(p => p.FindUserIdByRefIdInTenantAsync(MemberRef, TenantId, It.IsAny<CancellationToken>())).ReturnsAsync(MemberId);
        _accounts.Setup(p => p.GetByIdAsync(MemberId, It.IsAny<CancellationToken>())).ReturnsAsync(new User { Id = MemberId, RefId = MemberRef });

        _service = new TenantService(
            _tenants.Object, _accessControl.Object, _accounts.Object, _workspaces.Object, _bus, Mock.Of<IAuthMailer>(),
            NullLogger<TenantService>.Instance, new FixedTenantLimits());
    }

    [Fact]
    public async Task The_callers_tenants_are_listed_by_reference_and_name()
    {
        var acme = Guid.NewGuid();
        _tenants.Setup(p => p.GetTenantsForUserAsync(CallerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new Tenant { Id = TenantId, RefId = acme, Name = "Acme" }]);

        var result = await _service.GetTenantsForUserAsync(CallerId);

        Assert.Equal(new TenantResponse(acme, "Acme"), Assert.Single(result.Value));
    }

    // ---------------------------------------------------------------- invitations

    /// <summary>
    /// Accepting an invitation grants its role, so someone who may invite but not assign roles cannot
    /// use an invitation to hand out Admin, to a second address of their own or anyone else.
    /// </summary>
    [Fact]
    public async Task An_invitation_naming_a_role_needs_roles_manage()
    {
        var adminRef = Guid.NewGuid();
        _accessControl.Setup(p => p.FindRoleIdByRefIdAsync(adminRef, TenantId, It.IsAny<CancellationToken>())).ReturnsAsync(3);
        _accessControl.Setup(p => p.VerifyPermissionAsync(CallerId, TenantId, null, PermissionNames.RolesManage, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var withRole = await _service.CreateInvitationAsync(CallerId, TenantId, new CreateInvitationRequest("lead+2@example.test", adminRef));

        Assert.Equal("PERMISSION_UNAUTHORIZED", withRole.Error.Code);
        Assert.Equal(ErrorType.Forbidden, withRole.Error.Type);
        _tenants.Verify(p => p.CreateInvitationAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<byte[]>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<IReadOnlyCollection<int>>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task An_invitation_naming_a_role_is_issued_by_someone_who_may_assign_it()
    {
        var adminRef = Guid.NewGuid();
        _accessControl.Setup(p => p.FindRoleIdByRefIdAsync(adminRef, TenantId, It.IsAny<CancellationToken>())).ReturnsAsync(3);
        _accessControl.Setup(p => p.VerifyPermissionAsync(CallerId, TenantId, null, PermissionNames.RolesManage, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _tenants.Setup(p => p.CreateInvitationAsync(TenantId, "new@example.test", 3, It.IsAny<byte[]>(), It.IsAny<DateTime>(), CallerId, It.IsAny<IReadOnlyCollection<int>>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreatedInvitation(Guid.NewGuid(), "Acme"));

        var result = await _service.CreateInvitationAsync(CallerId, TenantId, new CreateInvitationRequest("new@example.test", adminRef));

        Assert.True(result.IsSuccess);
    }

    // ---------------------------------------------------------------- members

    [Fact]
    public async Task Removing_a_member_hands_their_workspaces_to_the_caller()
    {
        var result = await _service.RemoveTenantMemberAsync(CallerId, TenantId, MemberRef);

        Assert.True(result.IsSuccess);
        _tenants.Verify(p => p.RemoveTenantMemberAsync(TenantId, MemberId, CallerId, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Collection(_bus.Published,
            e => Assert.Equal(MemberId, Assert.IsType<UserPermissionsChangedEvent>(e).UserId),
            e => Assert.Equal(MemberRef, Assert.IsType<MemberRemovedEvent>(e).MemberRefId));
    }

    [Fact]
    public async Task Leaving_a_tenant_drops_cached_access_and_is_logged_in_that_tenant()
    {
        var tenantRef = Guid.NewGuid();
        _tenants.Setup(p => p.LeaveTenantAsync(tenantRef, MemberId, It.IsAny<CancellationToken>())).ReturnsAsync(TenantId);

        var result = await _service.LeaveTenantAsync(MemberId, tenantRef);

        Assert.True(result.IsSuccess);
        Assert.Collection(_bus.Published,
            e => Assert.Equal(MemberId, Assert.IsType<UserPermissionsChangedEvent>(e).UserId),
            e => Assert.Equal(MemberRef, Assert.IsType<MemberLeftEvent>(e).MemberRefId));
    }

    [Theory]
    [InlineData(50023, "TENANT_NOT_FOUND", ErrorType.Forbidden)]
    [InlineData(50024, "TENANT_LEAVE_LAST", ErrorType.Conflict)]
    [InlineData(50025, "TENANT_LEAVE_SOLE_MEMBER", ErrorType.Conflict)]
    [InlineData(50026, "TENANT_SOLE_ADMINISTRATOR", ErrorType.Conflict)]
    public async Task Leaving_is_refused_when_it_would_strand_someone(int guard, string code, ErrorType type)
    {
        _tenants.Setup(p => p.LeaveTenantAsync(It.IsAny<Guid>(), MemberId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(SqlExceptions.WithNumber(guard, "Nobody else administers Acme."));

        var result = await _service.LeaveTenantAsync(MemberId, Guid.NewGuid());

        Assert.Equal(code, result.Error.Code);
        Assert.Equal(type, result.Error.Type);
        Assert.Empty(_bus.Published);
    }

    [Fact]
    public async Task The_caller_cannot_remove_themselves()
    {
        var self = Guid.NewGuid();
        _tenants.Setup(p => p.FindUserIdByRefIdInTenantAsync(self, TenantId, It.IsAny<CancellationToken>())).ReturnsAsync(CallerId);

        var result = await _service.RemoveTenantMemberAsync(CallerId, TenantId, self);

        Assert.Equal("CANNOT_REMOVE_SELF", result.Error.Code);
        _tenants.Verify(p => p.RemoveTenantMemberAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_user_outside_the_tenant_reads_as_not_found()
    {
        var outsider = Guid.NewGuid();

        var removed = await _service.RemoveTenantMemberAsync(CallerId, TenantId, outsider);
        var suspended = await _service.SetMemberSuspendedAsync(CallerId, TenantId, outsider, true, Ip);

        foreach (var result in new[] { removed, suspended })
        {
            Assert.Equal("USER_NOT_FOUND", result.Error.Code);
            Assert.Equal(ErrorType.Forbidden, result.Error.Type);
        }

        _tenants.Verify(p => p.RemoveTenantMemberAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        _tenants.Verify(p => p.SetMemberSuspendedAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Empty(_bus.Published);
    }

    [Fact]
    public async Task The_caller_cannot_suspend_themselves_but_can_lift_their_own_suspension()
    {
        var self = Guid.NewGuid();
        _tenants.Setup(p => p.FindUserIdByRefIdInTenantAsync(self, TenantId, It.IsAny<CancellationToken>())).ReturnsAsync(CallerId);
        _accounts.Setup(p => p.GetByIdAsync(CallerId, It.IsAny<CancellationToken>())).ReturnsAsync(new User { Id = CallerId, RefId = self });

        var suspend = await _service.SetMemberSuspendedAsync(CallerId, TenantId, self, true, Ip);
        var lift = await _service.SetMemberSuspendedAsync(CallerId, TenantId, self, false, Ip);

        Assert.Equal("AUTH_CANNOT_SUSPEND_SELF", suspend.Error.Code);
        Assert.True(lift.IsSuccess);
        _tenants.Verify(p => p.SetMemberSuspendedAsync(TenantId, CallerId, true, It.IsAny<CancellationToken>()), Times.Never);
        _tenants.Verify(p => p.SetMemberSuspendedAsync(TenantId, CallerId, false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Suspending_a_member_raises_a_security_alert()
    {
        var result = await _service.SetMemberSuspendedAsync(CallerId, TenantId, MemberRef, true, Ip);

        Assert.True(result.IsSuccess);
        _tenants.Verify(p => p.SetMemberSuspendedAsync(TenantId, MemberId, true, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Single(_bus.Published.OfType<MemberSuspendedEvent>());
        var alert = Assert.Single(_bus.Published.OfType<SecurityAlertEvent>());
        Assert.Equal(("MemberSuspended", Ip), (alert.AlertType, alert.IpAddress));
    }

    // ---------------------------------------------------------------- invitations

    [Fact]
    public async Task An_invitation_naming_a_role_outside_the_tenant_is_refused()
    {
        var result = await _service.CreateInvitationAsync(CallerId, TenantId, new CreateInvitationRequest("amal@example.test", RoleRef: Guid.NewGuid()));

        Assert.Equal("ROLE_NOT_FOUND", result.Error.Code);
        AssertNoInvitationCreated();
    }

    [Fact]
    public async Task An_invitation_naming_an_unknown_workspace_is_refused()
    {
        var result = await _service.CreateInvitationAsync(CallerId, TenantId, new CreateInvitationRequest("amal@example.test", WorkspaceRefs: [Guid.NewGuid()]));

        Assert.Equal("WORKSPACE_NOT_FOUND", result.Error.Code);
        AssertNoInvitationCreated();
    }

    [Fact]
    public async Task Accepting_an_unknown_invitation_is_invalid()
    {
        _tenants.Setup(p => p.GetInvitationByTokenHashAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>())).ThrowsAsync(new SecurityException("No invitation."));

        var result = await _service.AcceptInvitationAsync(MemberId, "invitation-token");

        Assert.Equal("INVITATION_INVALID", result.Error.Code);
        _tenants.Verify(p => p.AcceptInvitationAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Accepting_an_invitation_answers_with_the_tenant_joined()
    {
        var invitationRef = Guid.NewGuid();
        var acme = Guid.NewGuid();
        _tenants.Setup(p => p.GetInvitationByTokenHashAsync(It.Is<byte[]>(h => h.SequenceEqual(SecurityTokens.Hash("invitation-token"))), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InvitationLookup(invitationRef, acme, "Acme", "amal@example.test", DateTime.UtcNow.AddDays(1), IsLive: true));
        _tenants.Setup(p => p.AcceptInvitationAsync(It.Is<byte[]>(h => h.SequenceEqual(SecurityTokens.Hash("invitation-token"))), MemberId, It.IsAny<int?>(), It.IsAny<CancellationToken>())).ReturnsAsync(TenantId);

        var result = await _service.AcceptInvitationAsync(MemberId, "invitation-token");

        Assert.Equal(new AcceptInvitationResponse(acme, "Acme"), result.Value);
        Assert.Single(_bus.Published.OfType<UserPermissionsChangedEvent>());
        var accepted = Assert.Single(_bus.Published.OfType<InvitationAcceptedEvent>());
        Assert.Equal((invitationRef, MemberId, MemberRef), (accepted.InvitationRefId, accepted.UserId, accepted.UserRefId));
    }

    private void AssertNoInvitationCreated()
    {
        _tenants.Verify(p => p.CreateInvitationAsync(
            It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<byte[]>(), It.IsAny<DateTime>(), It.IsAny<int>(),
            It.IsAny<IReadOnlyCollection<int>>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Empty(_bus.Published);
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
