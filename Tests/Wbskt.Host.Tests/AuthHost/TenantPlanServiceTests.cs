using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Host.Providers;
using Wbskt.Auth.Host.Services;
using Wbskt.Auth.Models;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Events.Outbox;
using Wbskt.Models.Billing;

namespace Wbskt.Host.Tests.AuthHost;

public sealed class TenantPlanServiceTests
{
    private static readonly Guid TenantRef = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly Mock<ITenantPlanProvider> _plans = new();
    private readonly Mock<IAuditWorkspaces> _audit = new();

    [Fact]
    public async Task An_unknown_tenant_is_not_found()
    {
        var result = await Service().SetAsync(TenantRef, new SetTenantPlanRequest(PlanCode.Pro), CancellationToken.None);

        Assert.Equal("TENANT_NOT_FOUND", result.Error.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    [Fact]
    public async Task An_undefined_plan_or_a_negative_limit_is_refused_before_anything_is_read()
    {
        var undefined = await Service().SetAsync(TenantRef, new SetTenantPlanRequest((PlanCode)42), CancellationToken.None);
        var negative = await Service().SetAsync(TenantRef, new SetTenantPlanRequest(PlanCode.Business, new PlanLimitOverrides { Clients = -5 }), CancellationToken.None);

        Assert.Equal("PLAN_CODE_INVALID", undefined.Error.Code);
        Assert.Equal("PLAN_LIMIT_INVALID", negative.Error.Code);
        _plans.Verify(p => p.FindByRefIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_change_queues_its_event_with_the_previous_plan_and_the_tenants_workspaces()
    {
        Current(PlanCode.Free, revision: 4);
        _audit.Setup(a => a.OfTenantAsync(9, It.IsAny<CancellationToken>())).ReturnsAsync([21, 22]);
        OutboxMessage? queued = null;
        _plans.Setup(p => p.SetAsync(9, PlanCode.Maker, null, 4, It.IsAny<OutboxMessage>(), It.IsAny<CancellationToken>()))
            .Callback<int, PlanCode, PlanLimitOverrides?, int, OutboxMessage, CancellationToken>((_, _, _, _, message, _) => queued = message)
            .ReturnsAsync(new TenantPlan(9, TenantRef, PlanCode.Maker, null, 5, DateTime.UtcNow));

        var result = await Service().SetAsync(TenantRef, new SetTenantPlanRequest(PlanCode.Maker), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(5, result.Value.PlanRevision);
        Assert.NotNull(queued);
        Assert.Equal("Wbskt.Events.Auth.TenantPlanChangedEvent", queued.MessageType);
        using JsonDocument payload = JsonDocument.Parse(queued.Payload);
        Assert.Equal("Maker", payload.RootElement.GetProperty("planCode").GetString());
        Assert.Equal("Free", payload.RootElement.GetProperty("previousPlanCode").GetString());
        Assert.Equal(5, payload.RootElement.GetProperty("planRevision").GetInt32());
        Assert.Equal([21, 22], payload.RootElement.GetProperty("workspaceIds").EnumerateArray().Select(e => e.GetInt32()));
    }

    [Fact]
    public async Task Asking_for_the_plan_already_held_changes_nothing()
    {
        Current(PlanCode.Business, revision: 2, overrides: new PlanLimitOverrides { Clients = 4_000 });

        var result = await Service().SetAsync(TenantRef, new SetTenantPlanRequest(PlanCode.Business, new PlanLimitOverrides { Clients = 4_000 }), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.PlanRevision);
        VerifyNotSet();
    }

    [Fact]
    public async Task Empty_overrides_count_as_none()
    {
        Current(PlanCode.Pro, revision: 1);

        var result = await Service().SetAsync(TenantRef, new SetTenantPlanRequest(PlanCode.Pro, new PlanLimitOverrides()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        VerifyNotSet();
    }

    [Fact]
    public async Task A_stale_expected_revision_is_a_conflict()
    {
        Current(PlanCode.Free, revision: 3);

        var result = await Service().SetAsync(TenantRef, new SetTenantPlanRequest(PlanCode.Pro, ExpectedRevision: 2), CancellationToken.None);

        Assert.Equal("TENANT_PLAN_CHANGED", result.Error.Code);
        VerifyNotSet();
    }

    [Fact]
    public async Task A_change_that_loses_a_race_is_a_conflict()
    {
        Current(PlanCode.Free, revision: 3);
        _plans.Setup(p => p.SetAsync(It.IsAny<int>(), It.IsAny<PlanCode>(), It.IsAny<PlanLimitOverrides?>(), It.IsAny<int>(), It.IsAny<OutboxMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TenantPlan?)null);

        var result = await Service().SetAsync(TenantRef, new SetTenantPlanRequest(PlanCode.Pro), CancellationToken.None);

        Assert.Equal("TENANT_PLAN_CHANGED", result.Error.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
    }

    [Fact]
    public async Task A_full_page_of_accounts_hands_out_the_last_workspace_as_its_cursor()
    {
        WorkspaceAccountResponse[] rows = [Account(3), Account(8)];
        _plans.Setup(p => p.GetWorkspaceAccountsAsync(2, 2, 9, null, It.IsAny<CancellationToken>())).ReturnsAsync(rows);

        var full = await Service().GetWorkspaceAccountsAsync(new PageRequest { Cursor = "2", Limit = 2 }, 9, null, CancellationToken.None);

        Assert.Equal("8", full.Value.NextCursor);

        _plans.Setup(p => p.GetWorkspaceAccountsAsync(8, 2, 9, null, It.IsAny<CancellationToken>())).ReturnsAsync([Account(12)]);
        var last = await Service().GetWorkspaceAccountsAsync(new PageRequest { Cursor = "8", Limit = 2 }, 9, null, CancellationToken.None);

        Assert.Null(last.Value.NextCursor);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("99999999999")]
    public async Task A_cursor_that_is_not_a_workspace_id_is_refused(string cursor)
    {
        var result = await Service().GetWorkspaceAccountsAsync(new PageRequest { Cursor = cursor }, null, null, CancellationToken.None);

        Assert.Equal(PageRequest.CursorInvalid.Code, result.Error.Code);
    }

    private void Current(PlanCode plan, int revision, PlanLimitOverrides? overrides = null) =>
        _plans.Setup(p => p.FindByRefIdAsync(TenantRef, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantPlan(9, TenantRef, plan, overrides, revision, null));

    private void VerifyNotSet() =>
        _plans.Verify(p => p.SetAsync(It.IsAny<int>(), It.IsAny<PlanCode>(), It.IsAny<PlanLimitOverrides?>(), It.IsAny<int>(), It.IsAny<OutboxMessage>(), It.IsAny<CancellationToken>()), Times.Never);

    private static WorkspaceAccountResponse Account(int workspaceId) => new(workspaceId, 9, TenantRef, PlanCode.Pro, null, 1);

    private TenantPlanService Service() => new(_plans.Object, _audit.Object, NullLogger<TenantPlanService>.Instance);
}
