using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Host.Providers;
using Wbskt.Auth.Host.Services;
using Wbskt.Auth.Models;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Events.Auth;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.StaffActions;
using Wbskt.Models.Admin;

namespace Wbskt.Host.Tests.AuthHost;

public sealed class AdminMemberServiceTests
{
    private static readonly StaffChange Staff = new(Guid.NewGuid(), "pika", "Owner asked on a call", Guid.NewGuid());
    private static readonly Guid TenantRef = Guid.NewGuid();
    private const int TenantId = 7;

    private readonly Mock<IStaffUserProvider> _staff = new();
    private readonly Mock<ITenantProvider> _tenants = new();
    private readonly Mock<IAccountProvider> _accounts = new();
    private readonly Mock<IEventBus> _bus = new();
    private readonly Mock<IAuditWorkspaces> _audit = new();
    private readonly RecordingRunner _runner = new();
    private readonly User _user = new() { Id = 42, RefId = Guid.NewGuid(), Username = "bob", Email = "bob@example.test", IsActive = true };

    public AdminMemberServiceTests()
    {
        _staff.Setup(s => s.FindTenantIdByRefIdAsync(TenantRef, It.IsAny<CancellationToken>())).ReturnsAsync(TenantId);
        _accounts.Setup(a => a.FindIdByRefIdAsync(_user.RefId, It.IsAny<CancellationToken>())).ReturnsAsync(_user.Id);
        _accounts.Setup(a => a.GetByIdAsync(_user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_user);
        _audit.Setup(a => a.OfTenantAsync(TenantId, It.IsAny<CancellationToken>())).ReturnsAsync([3, 4]);
    }

    private AdminMemberService Service() => new(_staff.Object, _tenants.Object, _accounts.Object, _bus.Object, _audit.Object, _runner,
        NullLogger<AdminMemberService>.Instance);

    [Fact]
    public async Task An_unknown_tenant_is_not_found_and_nothing_runs()
    {
        var result = await Service().SetSuspendedAsync(Guid.NewGuid(), _user.RefId, new AdminSetMemberSuspendedRequest(Staff, true), CancellationToken.None);

        Assert.Equal("TENANT_NOT_FOUND", result.Error.Code);
        Assert.Null(_runner.Action);
    }

    [Fact]
    public async Task An_unknown_person_is_not_a_member()
    {
        var result = await Service().RemoveAsync(TenantRef, Guid.NewGuid(), new AdminRemoveMemberRequest(Staff), CancellationToken.None);

        Assert.Equal("MEMBER_NOT_FOUND", result.Error.Code);
        Assert.Null(_runner.Action);
    }

    [Fact]
    public async Task A_suspension_is_audited_on_the_account_and_names_the_member()
    {
        var result = await Service().SetSuspendedAsync(TenantRef, _user.RefId, new AdminSetMemberSuspendedRequest(Staff, true), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new StaffAction(AdminMemberService.SuspendAction, AdminMemberService.TargetKind, TenantRef, $"Member bob ({_user.RefId})"), _runner.Action);
        _tenants.Verify(t => t.SetMemberSuspendedAsync(TenantId, 42, true, It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task A_suspension_is_announced_only_after_it_commits()
    {
        _runner.Inspect = () => _bus.VerifyNoOtherCalls();

        await Service().SetSuspendedAsync(TenantRef, _user.RefId, new AdminSetMemberSuspendedRequest(Staff, true), CancellationToken.None);

        _bus.Verify(b => b.PublishAsync(It.Is<UserPermissionsChangedEvent>(e => e.UserId == 42), It.IsAny<CancellationToken>()));
        _bus.Verify(b => b.PublishAsync(It.Is<TenantActorEvent>(e => e is MemberSuspendedEvent && ((MemberSuspendedEvent)e).MemberRefId == _user.RefId && e.WorkspaceIds.SequenceEqual(new[] { 3, 4 })), It.IsAny<CancellationToken>()));
        _bus.Verify(b => b.PublishAsync(It.Is<SecurityAlertEvent>(e => e.AlertType == "MemberSuspended"), It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task Lifting_a_suspension_raises_no_security_alert()
    {
        var result = await Service().SetSuspendedAsync(TenantRef, _user.RefId, new AdminSetMemberSuspendedRequest(Staff, false), CancellationToken.None);

        Assert.Equal(AdminMemberService.RestoreAction, _runner.Action!.Name);
        _bus.Verify(b => b.PublishAsync(It.Is<TenantActorEvent>(e => e is MemberUnsuspendedEvent), It.IsAny<CancellationToken>()));
        _bus.Verify(b => b.PublishAsync(It.IsAny<SecurityAlertEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Suspending_the_last_administrator_is_refused_inside_the_runner()
    {
        _tenants.Setup(t => t.SetMemberSuspendedAsync(TenantId, 42, true, It.IsAny<CancellationToken>()))
            .ThrowsAsync(SqlExceptions.WithNumber(50008, "This change would leave the tenant without an administrator."));

        var result = await Service().SetSuspendedAsync(TenantRef, _user.RefId, new AdminSetMemberSuspendedRequest(Staff, true), CancellationToken.None);

        Assert.Equal("TENANT_LAST_ADMINISTRATOR", result.Error.Code);
        Assert.NotNull(_runner.Action);
        _bus.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Removal_is_the_leave_procedure_acting_for_the_member()
    {
        var result = await Service().RemoveAsync(TenantRef, _user.RefId, new AdminRemoveMemberRequest(Staff), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AdminMemberService.RemoveAction, _runner.Action!.Name);
        _tenants.Verify(t => t.LeaveTenantAsync(TenantRef, 42, It.IsAny<CancellationToken>()));
        _bus.Verify(b => b.PublishAsync(It.Is<TenantActorEvent>(e => e is MemberRemovedEvent && ((MemberRemovedEvent)e).MemberRefId == _user.RefId), It.IsAny<CancellationToken>()));
    }

    [Theory]
    [InlineData(50023, "MEMBER_NOT_FOUND")]
    [InlineData(50024, "MEMBER_ONLY_TENANT")]
    [InlineData(50025, "TENANT_SOLE_MEMBER")]
    [InlineData(50026, "TENANT_LAST_ADMINISTRATOR")]
    public async Task The_leave_procedures_guards_become_refusals(int number, string code)
    {
        _tenants.Setup(t => t.LeaveTenantAsync(TenantRef, 42, It.IsAny<CancellationToken>())).ThrowsAsync(SqlExceptions.WithNumber(number, "guard"));

        var result = await Service().RemoveAsync(TenantRef, _user.RefId, new AdminRemoveMemberRequest(Staff), CancellationToken.None);

        Assert.Equal(code, result.Error.Code);
        _bus.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_replayed_removal_announces_nothing()
    {
        _runner.Replay = true;

        var result = await Service().RemoveAsync(TenantRef, _user.RefId, new AdminRemoveMemberRequest(Staff), CancellationToken.None);

        Assert.True(result.Value.Replayed);
        _tenants.VerifyNoOtherCalls();
        _bus.VerifyNoOtherCalls();
    }

    private sealed class RecordingRunner : IStaffActionRunner
    {
        public StaffAction? Action { get; private set; }

        public bool Replay { get; set; }

        public System.Action? Inspect { get; set; }

        public async Task<Result<StaffActionResponse>> RunAsync(StaffChange staff, StaffAction action, Func<CancellationToken, Task<Result>> change, CancellationToken cancellationToken)
        {
            Action = action;
            if (Replay)
            {
                return new StaffActionResponse(1, true);
            }

            var outcome = await change(cancellationToken);
            Inspect?.Invoke();
            return outcome.IsFailure ? outcome.Error : new StaffActionResponse(1, false);
        }
    }
}
