using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.Auth.Models;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Auth;
using Wbskt.Events.Client;
using Wbskt.Infrastructure.Billing;
using Wbskt.Infrastructure.Security;
using Wbskt.Management.Host.Handlers;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Providers;
using Wbskt.Management.Host.Services;
using Wbskt.Management.Host.Services.Billing;
using Wbskt.Management.Host.Services.Clients;
using Wbskt.Models;
using Wbskt.Models.Billing;
using Wbskt.Protocol;

namespace Wbskt.Host.Tests.ManagementHost;

public sealed class TenantFreezeHandlerTests
{
    private static readonly DateTimeOffset FrozenAt = new(2026, 11, 19, 8, 0, 0, TimeSpan.Zero);

    private readonly Mock<IWorkspaceAccountsClient> _auth = new();
    private readonly Mock<IWorkspaceAccountProvider> _accounts = new();
    private readonly Mock<IWorkspaceFreezeProvider> _provider = new();
    private readonly Mock<IEventBus> _bus = new();
    private readonly Mock<IWorkflowEngineGateway> _engine = new();
    private readonly Mock<IClientTokenCutoffs> _cutoffs = new();
    private readonly List<string> _order = [];

    public TenantFreezeHandlerTests()
    {
        foreach (int workspaceId in new[] { 3, 4 })
        {
            _auth.Setup(a => a.GetPageAsync(null, null, workspaceId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Page<WorkspaceAccountResponse> { Items = [new WorkspaceAccountResponse(workspaceId, 7, Guid.NewGuid(), PlanCode.Pro, null, 2, FrozenAt)] });
        }

        _accounts.Setup(a => a.UpsertAsync(It.IsAny<IReadOnlyCollection<WorkspaceAccount>>(), It.IsAny<CancellationToken>()))
            .Callback((IReadOnlyCollection<WorkspaceAccount> copies, CancellationToken _) => _order.Add($"copy {copies.Single().WorkspaceId}"))
            .ReturnsAsync(1);
        _provider.Setup(p => p.FreezeAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback((int workspaceId, CancellationToken _) => _order.Add($"freeze {workspaceId}"))
            .ReturnsAsync(new WorkspaceFreeze([], []));
    }

    private WorkspaceFreezer Freezer() => new(
        new WorkspaceAccountSync(_auth.Object, _accounts.Object, Mock.Of<IWorkspacePlans>(), NullLogger<WorkspaceAccountSync>.Instance),
        _provider.Object, _bus.Object, _engine.Object, _cutoffs.Object, NullLogger<WorkspaceFreezer>.Instance);

    [Fact]
    public async Task A_freeze_is_copied_before_each_workspace_is_stopped()
    {
        await new TenantFrozenHandler(Freezer()).Consume(Context(new TenantFrozenEvent(Guid.NewGuid()) { WorkspaceIds = [3, 4] }));

        // The copy goes first, so a run or enrolment racing the revoke is refused by the database.
        Assert.Equal(["copy 3", "freeze 3", "copy 4", "freeze 4"], _order);
        _accounts.Verify(a => a.UpsertAsync(It.Is<IReadOnlyCollection<WorkspaceAccount>>(c => c.Single().FrozenAt == FrozenAt), It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task A_frozen_workspace_drops_its_clients_and_cancels_its_runs()
    {
        var client = new RetiredClient(5, Guid.NewGuid(), 9, Guid.NewGuid());
        _provider.Setup(p => p.FreezeAsync(3, It.IsAny<CancellationToken>())).ReturnsAsync(new WorkspaceFreeze([client], [101L]));

        await new TenantFrozenHandler(Freezer()).Consume(Context(new TenantFrozenEvent(Guid.NewGuid()) { WorkspaceIds = [3] }));

        _bus.Verify(b => b.PublishAsync(
            It.Is<ClientStatusChangedEvent>(e => e.ClientRefId == client.ClientRefId && e.WorkspaceId == 3 && e.Status == (byte)ClientStatus.Revoked),
            It.IsAny<CancellationToken>()), Times.Once);
        _cutoffs.Verify(c => c.RevokeAsync(client.ClientRefId), Times.Once);
        _engine.Verify(e => e.CancelRunAsync(101L, WorkspaceFreezer.CancellationReason, It.IsAny<CancellationToken>()), Times.Once);
        _engine.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Lifting_a_freeze_only_copies_it()
    {
        await new TenantUnfrozenHandler(Freezer()).Consume(Context(new TenantUnfrozenEvent(Guid.NewGuid()) { WorkspaceIds = [3, 4] }));

        Assert.Equal(["copy 3", "copy 4"], _order);
        _provider.VerifyNoOtherCalls();
        _engine.VerifyNoOtherCalls();
    }

    private static ConsumeContext<T> Context<T>(T message) where T : class
    {
        var context = new Mock<ConsumeContext<T>>();
        context.SetupGet(c => c.Message).Returns(message);
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        return context.Object;
    }
}
