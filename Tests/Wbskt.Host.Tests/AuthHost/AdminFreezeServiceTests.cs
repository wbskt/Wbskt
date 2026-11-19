using Microsoft.Extensions.Logging.Abstractions;
using Moq;
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

public sealed class AdminFreezeServiceTests
{
    private static readonly StaffChange Staff = new(Guid.NewGuid(), "pika", "Chargeback under review", Guid.NewGuid());
    private static readonly Guid TenantRef = Guid.NewGuid();
    private const int TenantId = 7;

    private readonly Mock<IStaffUserProvider> _staff = new();
    private readonly Mock<IAccountProvider> _accounts = new();
    private readonly Mock<IEventBus> _bus = new();
    private readonly RecordingAccessChanges _accessChanges = new();
    private readonly Mock<IAuditWorkspaces> _audit = new();
    private readonly RecordingRunner _runner = new();

    public AdminFreezeServiceTests()
    {
        _staff.Setup(s => s.FindTenantIdByRefIdAsync(TenantRef, It.IsAny<CancellationToken>())).ReturnsAsync(TenantId);
        _audit.Setup(a => a.OfTenantAsync(TenantId, It.IsAny<CancellationToken>())).ReturnsAsync([3, 4]);
    }

    private AdminFreezeService Service() => new(_staff.Object, _runner,
        new TenantAnnouncer(_bus.Object, _audit.Object, _accessChanges.Changes, NullLogger<TenantAnnouncer>.Instance),
        NullLogger<AdminFreezeService>.Instance);

    [Fact]
    public async Task An_unknown_tenant_is_not_found_and_nothing_runs()
    {
        var result = await Service().SetFrozenAsync(Guid.NewGuid(), new AdminSetTenantFrozenRequest(Staff, true), CancellationToken.None);

        Assert.Equal("TENANT_NOT_FOUND", result.Error.Code);
        Assert.Null(_runner.Action);
    }

    [Fact]
    public async Task A_freeze_is_audited_and_announced_to_every_workspace_after_it_commits()
    {
        _runner.Inspect = () =>
        {
            _bus.VerifyNoOtherCalls();
            Assert.Equal(0, _accessChanges.Announced);
        };

        var result = await Service().SetFrozenAsync(TenantRef, new AdminSetTenantFrozenRequest(Staff, true), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new StaffAction(AdminFreezeService.FreezeAction, AdminFreezeService.TargetKind, TenantRef), _runner.Action);
        _staff.Verify(s => s.SetTenantFrozenAsync(TenantId, true, It.IsAny<CancellationToken>()));

        // Dropping the management host's cached access is what makes its workspaces read-only at once.
        Assert.Equal(1, _accessChanges.Announced);
        _bus.Verify(b => b.PublishAsync(It.Is<TenantActorEvent>(e => e is TenantFrozenEvent
            && ((TenantFrozenEvent)e).TenantRefId == TenantRef
            && e.WorkspaceIds.SequenceEqual(new[] { 3, 4 })
            && e.ActorSource == EventSource.Support), It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task Lifting_a_freeze_raises_the_unfrozen_event()
    {
        var result = await Service().SetFrozenAsync(TenantRef, new AdminSetTenantFrozenRequest(Staff, false), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AdminFreezeService.UnfreezeAction, _runner.Action!.Name);
        _bus.Verify(b => b.PublishAsync(It.Is<TenantActorEvent>(e => e is TenantUnfrozenEvent), It.IsAny<CancellationToken>()));
    }

    [Theory]
    [InlineData(true, "ACCOUNT_ALREADY_FROZEN")]
    [InlineData(false, "ACCOUNT_NOT_FROZEN")]
    public async Task A_change_to_the_state_it_is_in_is_refused_and_announces_nothing(bool frozen, string code)
    {
        _staff.Setup(s => s.SetTenantFrozenAsync(TenantId, frozen, It.IsAny<CancellationToken>())).ThrowsAsync(SqlExceptions.WithNumber(50049, "same state"));

        var result = await Service().SetFrozenAsync(TenantRef, new AdminSetTenantFrozenRequest(Staff, frozen), CancellationToken.None);

        Assert.Equal(code, result.Error.Code);
        Assert.Equal(0, _accessChanges.Announced);
        _bus.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_replay_announces_nothing()
    {
        _runner.Replay = true;

        var result = await Service().SetFrozenAsync(TenantRef, new AdminSetTenantFrozenRequest(Staff, true), CancellationToken.None);

        Assert.True(result.Value.Replayed);
        Assert.Equal(0, _accessChanges.Announced);
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
