using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Host.Providers;
using Wbskt.Auth.Host.Services;
using Wbskt.Auth.Host.Telemetry;
using Wbskt.Auth.Models;
using Wbskt.EventBus.Abstractions;

namespace Wbskt.Host.Tests.AuthHost;

/// <summary>The workspaces list labels the caller's place in each workspace and counts its members (#666).</summary>
public sealed class WorkspaceListTests
{
    private const int UserId = 7;

    [Fact]
    public async Task Each_workspace_says_the_callers_role_with_owner_before_admin()
    {
        var workspaces = new Mock<IWorkspaceProvider>();
        workspaces.Setup(w => w.GetWorkspacesForUserAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new MemberWorkspace(Workspace("Greenhouse", ownerUserId: UserId), IsAdmin: true, MemberCount: 3),
            new MemberWorkspace(Workspace("Garage", ownerUserId: 9), IsAdmin: true, MemberCount: 2),
            new MemberWorkspace(Workspace("Shed", ownerUserId: 9), IsAdmin: false, MemberCount: 5)
        ]);
        var service = new WorkspaceService(workspaces.Object, Mock.Of<IAccountProvider>(), Mock.Of<ITenantProvider>(), Mock.Of<IAccessControlProvider>(), Mock.Of<IEventBus>(), NullLogger<WorkspaceService>.Instance, new AuthMetrics(), new FixedTenantLimits(), WorkspaceFakes.Created());

        var listed = (await service.GetWorkspacesForUserAsync(UserId)).Value;

        Assert.Equal(
            [("Greenhouse", WorkspaceRole.Owner, 3), ("Garage", WorkspaceRole.Admin, 2), ("Shed", WorkspaceRole.Member, 5)],
            listed.Select(w => (w.Name, w.Role, w.MemberCount)));
    }

    private static Workspace Workspace(string name, int ownerUserId) => new()
    {
        Id = Random.Shared.Next(1, 1000),
        RefId = Guid.NewGuid(),
        Name = name,
        OwnerUserId = ownerUserId,
        TenantId = 1,
        TenantRefId = Guid.NewGuid(),
        CreatedAt = DateTime.UtcNow
    };
}
