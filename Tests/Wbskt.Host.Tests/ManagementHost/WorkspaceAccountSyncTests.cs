using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.Auth.Models;
using Wbskt.Infrastructure.Billing;
using Wbskt.Management.Host.Services.Billing;
using Wbskt.Management.Host.Services.Clients;
using Wbskt.Models;
using Wbskt.Models.Billing;

namespace Wbskt.Host.Tests.ManagementHost;

public sealed class WorkspaceAccountSyncTests
{
    private static readonly Guid TenantRef = Guid.NewGuid();

    private readonly Mock<IWorkspaceAccountsClient> _auth = new();
    private readonly Mock<IWorkspaceAccountProvider> _accounts = new();
    private readonly Mock<IWorkspacePlans> _plans = new();

    [Fact]
    public async Task A_full_sync_reads_every_page_and_copies_each_one()
    {
        _auth.Setup(a => a.GetPageAsync(null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Page<WorkspaceAccountResponse> { Items = [Account(1), Account(2)], NextCursor = "2" });
        _auth.Setup(a => a.GetPageAsync("2", null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Page<WorkspaceAccountResponse> { Items = [Account(5)] });
        _accounts.Setup(a => a.UpsertAsync(It.IsAny<IReadOnlyCollection<WorkspaceAccount>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<WorkspaceAccount> copies, CancellationToken _) => copies.Count);

        int changed = await Sync().SyncAllAsync(CancellationToken.None);

        Assert.Equal(3, changed);
        _accounts.Verify(a => a.UpsertAsync(It.Is<IReadOnlyCollection<WorkspaceAccount>>(c => c.Select(x => x.WorkspaceId).SequenceEqual(new[] { 1, 2 })), It.IsAny<CancellationToken>()));
        _accounts.Verify(a => a.UpsertAsync(It.Is<IReadOnlyCollection<WorkspaceAccount>>(c => c.Count == 1 && c.First().WorkspaceId == 5 && c.First().PlanCode == PlanCode.Pro), It.IsAny<CancellationToken>()));
        _plans.Verify(p => p.Invalidate(1));
        _plans.Verify(p => p.Invalidate(2));
        _plans.Verify(p => p.Invalidate(5));
    }

    [Fact]
    public async Task A_tenant_sync_asks_only_for_that_tenant()
    {
        _auth.Setup(a => a.GetPageAsync(null, 9, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Page<WorkspaceAccountResponse> { Items = [Account(4)] });

        await Sync().SyncTenantAsync(9, CancellationToken.None);

        _auth.Verify(a => a.GetPageAsync(null, 9, null, It.IsAny<CancellationToken>()), Times.Once);
        _auth.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_workspace_sync_asks_only_for_that_workspace()
    {
        _auth.Setup(a => a.GetPageAsync(null, null, 4, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Page<WorkspaceAccountResponse> { Items = [Account(4)] });

        await Sync().SyncWorkspaceAsync(4, CancellationToken.None);

        _auth.Verify(a => a.GetPageAsync(null, null, 4, It.IsAny<CancellationToken>()), Times.Once);
        _plans.Verify(p => p.Invalidate(4));
    }

    [Fact]
    public async Task The_auth_host_failing_fails_the_sync()
    {
        _auth.Setup(a => a.GetPageAsync(It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("down"));

        await Assert.ThrowsAsync<HttpRequestException>(() => Sync().SyncAllAsync(CancellationToken.None));
        _accounts.VerifyNoOtherCalls();
    }

    private static WorkspaceAccountResponse Account(int workspaceId) => new(workspaceId, 9, TenantRef, PlanCode.Pro, null, 3);

    private WorkspaceAccountSync Sync() => new(_auth.Object, _accounts.Object, _plans.Object, NullLogger<WorkspaceAccountSync>.Instance);
}
