using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Security;
using Wbskt.Management.Host.Hubs;
using Wbskt.Management.Host.Services.Clients;
using Wbskt.Primitives.Constants;

namespace Wbskt.Host.Tests.ManagementHost;

/// <summary>
/// A console connection keeps only the feeds its token may still read (#784): after an access change
/// each joined workspace and client feed is resolved again, and one now refused is left.
/// </summary>
public sealed class LiveFeedAccessRecheckTests
{
    private const string Token = "member-token";
    private static readonly Guid Kept = Guid.NewGuid();
    private static readonly Guid Lost = Guid.NewGuid();

    private readonly Mock<IAuthServiceClient> _auth = new();
    private readonly Mock<IGroupManager> _groups = new();
    private readonly LiveFeedConnections _connections = new();
    private readonly WorkspaceAccessCache _cache = new(new ConfigurationBuilder().Build());
    private readonly AccessTokenRevocation _revocation = new(null, Options.Create(new AccessTokenOptions()), TimeProvider.System, NullLogger<AccessTokenRevocation>.Instance);
    private readonly LiveFeedAccessRecheck _recheck;

    public LiveFeedAccessRecheckTests()
    {
        var hub = new Mock<IHubContext<NotificationHub>>();
        hub.Setup(h => h.Groups).Returns(_groups.Object);
        var services = new ServiceCollection().AddSingleton(_auth.Object).BuildServiceProvider();
        _recheck = new LiveFeedAccessRecheck(_cache, _revocation, _connections, hub.Object, services.GetRequiredService<IServiceScopeFactory>(), NullLogger<LiveFeedAccessRecheck>.Instance);

        Answer(Kept, Result<WorkspaceAccess>.Success(Access(1, PermissionNames.WorkspaceJoin, PermissionNames.LogsRead)));
        Answer(Lost, Result<WorkspaceAccess>.Failure(Error.Forbidden("WORKSPACE_FORBIDDEN", "Access denied to workspace.")));
    }

    [Fact]
    public async Task A_workspace_the_member_was_removed_from_is_left_and_one_still_theirs_is_kept()
    {
        var connection = _connections.Open("c1", Token);
        connection.Workspaces[Kept] = 1;
        connection.Workspaces[Lost] = 2;
        connection.Clients[Guid.NewGuid()] = new WatchedClient(Lost, 20);

        await _recheck.RecheckAllAsync(CancellationToken.None);

        connection.Workspaces.Keys.Should().Equal(Kept);
        connection.Clients.Should().BeEmpty();
        _groups.Verify(g => g.RemoveFromGroupAsync("c1", NotificationHub.GroupName(2), It.IsAny<CancellationToken>()), Times.Once);
        _groups.Verify(g => g.RemoveFromGroupAsync("c1", NotificationHub.ClientGroupName(20), It.IsAny<CancellationToken>()), Times.Once);
        _groups.Verify(g => g.RemoveFromGroupAsync("c1", NotificationHub.GroupName(1), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_client_feed_goes_when_logs_read_does_though_the_workspace_stays()
    {
        var workspace = Guid.NewGuid();
        Answer(workspace, Result<WorkspaceAccess>.Success(Access(3, PermissionNames.WorkspaceJoin)));
        var connection = _connections.Open("c1", Token);
        connection.Workspaces[workspace] = 3;
        connection.Clients[Guid.NewGuid()] = new WatchedClient(workspace, 30);

        await _recheck.RecheckAllAsync(CancellationToken.None);

        connection.Workspaces.Should().ContainKey(workspace);
        connection.Clients.Should().BeEmpty();
    }

    [Fact]
    public async Task An_auth_host_that_cannot_answer_takes_nothing_away()
    {
        var workspace = Guid.NewGuid();
        Answer(workspace, Result<WorkspaceAccess>.Failure(AuthServiceClient.AuthUnavailable));
        var connection = _connections.Open("c1", Token);
        connection.Workspaces[workspace] = 4;

        await _recheck.RecheckAllAsync(CancellationToken.None);

        connection.Workspaces.Should().ContainKey(workspace);
        _groups.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task An_access_change_starts_a_pass()
    {
        await _recheck.StartAsync(CancellationToken.None);
        var connection = _connections.Open("c1", Token);
        connection.Workspaces[Lost] = 2;

        _cache.Clear();
        await _recheck.Pass;

        connection.Workspaces.Should().BeEmpty();
        await _recheck.StopAsync(CancellationToken.None);
    }

    /// <summary>
    /// Signing out everywhere or changing the password revokes the token a connection opened with, and
    /// the auth host then answers 401 for it; the revocation alone starts a pass (#959).
    /// </summary>
    [Fact]
    public async Task A_revoked_token_loses_its_feeds_without_any_access_change()
    {
        var workspace = Guid.NewGuid();
        Answer(workspace, Result<WorkspaceAccess>.Failure(Error.Unauthorized("TOKEN_REVOKED", "The token was revoked.")));
        await _recheck.StartAsync(CancellationToken.None);
        var user = Guid.NewGuid();
        var connection = _connections.Open("c1", Token, user);
        connection.Workspaces[workspace] = 5;
        connection.Clients[Guid.NewGuid()] = new WatchedClient(workspace, 50);

        await _revocation.RevokeUserAsync(user);
        await _recheck.Pass;

        connection.Workspaces.Should().BeEmpty();
        connection.Clients.Should().BeEmpty();
        _groups.Verify(g => g.RemoveFromGroupAsync("c1", NotificationHub.GroupName(5), It.IsAny<CancellationToken>()), Times.Once);
        await _recheck.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task A_token_revoked_here_loses_its_feeds_without_asking_the_auth_host()
    {
        // #1081: the revocation is already in memory; one call per workspace to hear 401 was waste.
        var user = Guid.NewGuid();
        var issued = DateTimeOffset.UtcNow.AddMinutes(-1);
        var signedOut = _connections.Open("signed-out", Token, user, Guid.NewGuid(), issued);
        signedOut.Workspaces[Kept] = 1;
        signedOut.Clients[Guid.NewGuid()] = new WatchedClient(Kept, 10);
        var sessionEnded = _connections.Open("session-ended", Token, Guid.NewGuid(), Guid.NewGuid(), issued);
        sessionEnded.Workspaces[Kept] = 1;

        await _revocation.RevokeUserAsync(user);
        await _revocation.RevokeSessionAsync(sessionEnded.SessionId!.Value);
        await _recheck.RecheckAllAsync(CancellationToken.None);

        signedOut.Workspaces.Should().BeEmpty();
        signedOut.Clients.Should().BeEmpty();
        sessionEnded.Workspaces.Should().BeEmpty();
        _groups.Verify(g => g.RemoveFromGroupAsync("signed-out", NotificationHub.ClientGroupName(10), It.IsAny<CancellationToken>()), Times.Once);
        _auth.Verify(a => a.ResolveWorkspaceForTokenAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// A change to one member re-checks that member's connections and those joined to the workspace it
    /// names, not every console open on the instance (#1001).
    /// </summary>
    [Fact]
    public async Task A_change_naming_someone_else_passes_a_connection_by()
    {
        var member = Guid.NewGuid();
        var mine = _connections.Open("mine", Token, member);
        mine.Workspaces[Lost] = 2;
        var theirs = _connections.Open("theirs", Token, Guid.NewGuid());
        theirs.Workspaces[Lost] = 2;

        await _recheck.RecheckAsync(AccessChange.ForUsers(member), CancellationToken.None);

        mine.Workspaces.Should().BeEmpty();
        theirs.Workspaces.Should().ContainKey(Lost);
    }

    [Fact]
    public async Task A_change_naming_a_workspace_reaches_a_connection_watching_one_of_its_clients()
    {
        var watching = _connections.Open("watching", Token, Guid.NewGuid());
        watching.Clients[Guid.NewGuid()] = new WatchedClient(Lost, 20);
        var elsewhere = _connections.Open("elsewhere", Token, Guid.NewGuid());
        elsewhere.Workspaces[Kept] = 1;

        await _recheck.RecheckAsync(AccessChange.ForWorkspaces(Lost), CancellationToken.None);

        watching.Clients.Should().BeEmpty();
        _auth.Verify(a => a.ResolveWorkspaceForTokenAsync(Token, Kept, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Ending_one_session_passes_the_same_user_in_another_by()
    {
        var user = Guid.NewGuid();
        var ended = _connections.Open("ended", Token, user, Guid.NewGuid());
        ended.Workspaces[Lost] = 2;
        var other = _connections.Open("other", Token, user, Guid.NewGuid());
        other.Workspaces[Lost] = 2;

        await _recheck.RecheckAsync(AccessChange.ForSession(ended.SessionId!.Value), CancellationToken.None);

        ended.Workspaces.Should().BeEmpty();
        other.Workspaces.Should().ContainKey(Lost);
    }

    private void Answer(Guid workspaceRef, Result<WorkspaceAccess> answer) =>
        _auth.Setup(a => a.ResolveWorkspaceForTokenAsync(Token, workspaceRef, It.IsAny<CancellationToken>())).ReturnsAsync(answer);

    private static WorkspaceAccess Access(int workspaceId, params string[] permissions) =>
        new(workspaceId, permissions.ToHashSet(StringComparer.OrdinalIgnoreCase));
}
