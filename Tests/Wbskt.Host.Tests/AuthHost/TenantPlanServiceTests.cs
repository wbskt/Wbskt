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
        var result = await Service().SetAsync(TenantRef, new SetTenantPlanRequest(PlanCode.Pro), PlanSource.Operator, CancellationToken.None);

        Assert.Equal("TENANT_NOT_FOUND", result.Error.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    [Fact]
    public async Task An_undefined_plan_or_a_negative_limit_is_refused_before_anything_is_read()
    {
        var undefined = await Service().SetAsync(TenantRef, new SetTenantPlanRequest((PlanCode)42), PlanSource.Operator, CancellationToken.None);
        var negative = await Service().SetAsync(TenantRef, new SetTenantPlanRequest(PlanCode.Business, new PlanLimitOverrides { Clients = -5 }), PlanSource.Operator, CancellationToken.None);

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
        _plans.Setup(p => p.SetAsync(9, PlanCode.Maker, null, 4, PlanSource.Operator, null, It.IsAny<OutboxMessage>(), It.IsAny<CancellationToken>()))
            .Callback<int, PlanCode, PlanLimitOverrides?, int, PlanSource, DateTime?, OutboxMessage, CancellationToken>((_, _, _, _, _, _, message, _) => queued = message)
            .ReturnsAsync(new TenantPlan(9, TenantRef, PlanCode.Maker, null, 5, DateTime.UtcNow));

        var result = await Service().SetAsync(TenantRef, new SetTenantPlanRequest(PlanCode.Maker), PlanSource.Operator, CancellationToken.None);

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
    public async Task An_operator_business_plan_carries_its_source_end_and_overrides_into_the_audited_event()
    {
        Current(PlanCode.Free, revision: 0);
        var endsAt = new DateTimeOffset(2027, 6, 30, 0, 0, 0, TimeSpan.Zero);
        var overrides = new PlanLimitOverrides { Clients = 4_000 };
        OutboxMessage? queued = null;
        _plans.Setup(p => p.SetAsync(9, PlanCode.Business, overrides, 0, PlanSource.Operator, endsAt.UtcDateTime, It.IsAny<OutboxMessage>(), It.IsAny<CancellationToken>()))
            .Callback<int, PlanCode, PlanLimitOverrides?, int, PlanSource, DateTime?, OutboxMessage, CancellationToken>((_, _, _, _, _, _, message, _) => queued = message)
            .ReturnsAsync(new TenantPlan(9, TenantRef, PlanCode.Business, overrides, 1, DateTime.UtcNow));

        var result = await Service().SetAsync(TenantRef, new SetTenantPlanRequest(PlanCode.Business, overrides, EndsAt: endsAt), PlanSource.Operator, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(queued);
        using JsonDocument payload = JsonDocument.Parse(queued.Payload);
        Assert.Equal("Operator", payload.RootElement.GetProperty("source").GetString());
        Assert.Equal(endsAt, payload.RootElement.GetProperty("endsAt").GetDateTimeOffset());
        Assert.Equal(4_000, payload.RootElement.GetProperty("limitOverrides").GetProperty("clients").GetInt32());
    }

    [Fact]
    public async Task Asking_for_the_plan_already_held_changes_nothing()
    {
        Current(PlanCode.Business, revision: 2, overrides: new PlanLimitOverrides { Clients = 4_000 });

        var result = await Service().SetAsync(TenantRef, new SetTenantPlanRequest(PlanCode.Business, new PlanLimitOverrides { Clients = 4_000 }), PlanSource.Operator, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.PlanRevision);
        VerifyNotSet();
    }

    [Fact]
    public async Task Empty_overrides_count_as_none()
    {
        Current(PlanCode.Pro, revision: 1);

        var result = await Service().SetAsync(TenantRef, new SetTenantPlanRequest(PlanCode.Pro, new PlanLimitOverrides()), PlanSource.Operator, CancellationToken.None);

        Assert.True(result.IsSuccess);
        VerifyNotSet();
    }

    [Fact]
    public async Task A_stale_expected_revision_is_a_conflict()
    {
        Current(PlanCode.Free, revision: 3);

        var result = await Service().SetAsync(TenantRef, new SetTenantPlanRequest(PlanCode.Pro, ExpectedRevision: 2), PlanSource.Operator, CancellationToken.None);

        Assert.Equal("TENANT_PLAN_CHANGED", result.Error.Code);
        VerifyNotSet();
    }

    [Fact]
    public async Task A_change_that_loses_a_race_is_a_conflict()
    {
        Current(PlanCode.Free, revision: 3);
        _plans.Setup(p => p.SetAsync(It.IsAny<int>(), It.IsAny<PlanCode>(), It.IsAny<PlanLimitOverrides?>(), It.IsAny<int>(), It.IsAny<PlanSource>(), It.IsAny<DateTime?>(), It.IsAny<OutboxMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TenantPlan?)null);

        var result = await Service().SetAsync(TenantRef, new SetTenantPlanRequest(PlanCode.Pro), PlanSource.Operator, CancellationToken.None);

        Assert.Equal("TENANT_PLAN_CHANGED", result.Error.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
    }

    [Fact]
    public async Task An_operators_plan_keeps_its_end_date_and_the_subscriptions_never_does()
    {
        Current(PlanCode.Free, revision: 2);
        var endsAt = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero);
        _plans.Setup(p => p.SetAsync(9, PlanCode.Business, null, 2, It.IsAny<PlanSource>(), It.IsAny<DateTime?>(), It.IsAny<OutboxMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int _, PlanCode plan, PlanLimitOverrides? _, int _, PlanSource source, DateTime? ends, OutboxMessage _, CancellationToken _) =>
                new TenantPlan(9, TenantRef, plan, null, 3, DateTime.UtcNow, source, ends));

        var operatorPlan = await Service().SetAsync(TenantRef, new SetTenantPlanRequest(PlanCode.Business, EndsAt: endsAt), PlanSource.Operator, CancellationToken.None);
        var subscriptionPlan = await Service().SetAsync(TenantRef, new SetTenantPlanRequest(PlanCode.Business, EndsAt: endsAt), PlanSource.Subscription, CancellationToken.None);

        Assert.Equal(PlanSource.Operator, operatorPlan.Value.Source);
        Assert.Equal(endsAt, operatorPlan.Value.EndsAt);
        Assert.Equal(PlanSource.Subscription, subscriptionPlan.Value.Source);
        Assert.Null(subscriptionPlan.Value.EndsAt);
    }

    [Fact]
    public async Task The_same_plan_from_another_source_is_still_written_so_the_source_is_recorded()
    {
        Current(PlanCode.Pro, revision: 2, source: PlanSource.Subscription);
        _plans.Setup(p => p.SetAsync(9, PlanCode.Pro, null, 2, PlanSource.Operator, null, It.IsAny<OutboxMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantPlan(9, TenantRef, PlanCode.Pro, null, 3, DateTime.UtcNow, PlanSource.Operator));

        var result = await Service().SetAsync(TenantRef, new SetTenantPlanRequest(PlanCode.Pro), PlanSource.Operator, CancellationToken.None);

        Assert.Equal(3, result.Value.PlanRevision);
        Assert.Equal(PlanSource.Operator, result.Value.Source);
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

    private void Current(PlanCode plan, int revision, PlanLimitOverrides? overrides = null, PlanSource source = PlanSource.Operator) =>
        _plans.Setup(p => p.FindByRefIdAsync(TenantRef, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantPlan(9, TenantRef, plan, overrides, revision, null, source));

    private void VerifyNotSet() =>
        _plans.Verify(p => p.SetAsync(It.IsAny<int>(), It.IsAny<PlanCode>(), It.IsAny<PlanLimitOverrides?>(), It.IsAny<int>(), It.IsAny<PlanSource>(), It.IsAny<DateTime?>(), It.IsAny<OutboxMessage>(), It.IsAny<CancellationToken>()), Times.Never);

    private static WorkspaceAccountResponse Account(int workspaceId) => new(workspaceId, 9, TenantRef, PlanCode.Pro, null, 1);

    private TenantPlanService Service() => new(_plans.Object, _audit.Object, NullLogger<TenantPlanService>.Instance);
}
