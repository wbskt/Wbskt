using System.Net;
using System.Net.WebSockets;
using FluentAssertions;
using Wbskt.Auth.Models;
using Wbskt.E2E.FeatureTests.Fixtures;

namespace Wbskt.E2E.FeatureTests.Scenarios.Outage;

/// <summary>
/// Redis goes away under running hosts, and comes back. Redis holds two things the hosts share, and
/// both are documented as best effort, so neither may fail open while it is gone:
/// <list type="bullet">
/// <item>
/// The per-device token cutoffs (ClientTokenCutoffs). With Redis unreachable the socket host's read
/// answers "don't know" and ClientAccessGate falls back to its own revocation list, which the
/// revocation events fill; the management host still sends the event when it cannot write the
/// cutoff. So a device revoked before the outage, and one revoked during it, are both refused at the
/// socket, and a device nobody revoked still gets in.
/// </item>
/// <item>
/// The workspace-access change feed (WorkspaceAccessChanges) that lets the management host keep a
/// resolved access for up to <c>WorkspaceAccessCache:ConfirmedSeconds</c>. Without it nothing is
/// announced, and the cache serves an entry for no longer than <c>WorkspaceAccessCache:Seconds</c>
/// (30 s by default, and in Development), so a member removed during the outage loses access within
/// that bound instead of at once.
/// </item>
/// </list>
/// Runs only with <c>E2E_OUTAGE_COMMAND</c> (see <see cref="OutageControl"/>), on its own.
/// </summary>
[Collection(EngineCollection.Name)]
public sealed class RedisOutageE2ETests(ServicesFixture fixture)
{
    // WorkspaceAccessCache:Seconds (30 s), plus the two missed version checks (5 s apart) after which
    // a confirmation lapses, plus room for a slow runner.
    private static readonly TimeSpan AccessCacheBound = TimeSpan.FromSeconds(90);

    [SkippableFact]
    public async Task OUT_REDIS_01_RevokedDevices_AreStillRefusedAtTheSocket()
    {
        Skip.IfNot(E2EConfig.OutageTestsEnabled, OutageControl.SkipReason);
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspaceRef) = await fixture.CreateIsolatedWorkspaceAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspaceRef);

        var (beforeRef, beforeSecret) = await fixture.RegisterClientAsync(pin, "revoked-before-the-outage");
        var (duringRef, duringSecret) = await fixture.RegisterClientAsync(pin, "revoked-during-the-outage");
        var (untouchedRef, untouchedSecret) = await fixture.RegisterClientAsync(pin, "untouched");
        var beforeToken = await fixture.LoginClientAsync(beforeRef, beforeSecret);
        var duringToken = await fixture.LoginClientAsync(duringRef, duringSecret);
        var untouchedToken = await fixture.LoginClientAsync(untouchedRef, untouchedSecret);

        // Revoked while Redis is up, with a connection open: the socket host closing that connection
        // is what shows its own list has the revocation, which is all it has once Redis is gone.
        using (var live = await OpenAsync(beforeToken))
        {
            (await RevokeAsync(workspaceRef, beforeRef, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
            await WaitForServerCloseAsync(live, "the revocation event closes the device's live connection");
        }

        await OutageControl.RunWithRestoreAsync(OutageControl.RedisStart, async () =>
        {
            await OutageControl.RunAsync(OutageControl.RedisStop);

            (await ConnectAsync(beforeToken)).Should().Be(HttpStatusCode.Forbidden,
                "the socket host falls back to its own revocation list when Redis cannot answer");

            // The management host cannot write the cutoff now, and says so in its log; the event it
            // sends regardless is the only way the socket host hears of this one.
            (await RevokeAsync(workspaceRef, duringRef, token)).StatusCode.Should().Be(HttpStatusCode.NoContent,
                "revoking a device does not depend on Redis");
            await ServicesFixture.PollUntilAsync(
                "the device revoked during the Redis outage to be refused at the socket",
                async () =>
                {
                    var status = await ConnectAsync(duringToken);
                    return (status == HttpStatusCode.Forbidden, $"upgrade answered {(int)status}");
                },
                TimeSpan.FromSeconds(30),
                TimeSpan.FromSeconds(1));

            (await ConnectAsync(untouchedToken)).Should().Be(HttpStatusCode.SwitchingProtocols,
                "a device nobody revoked is not locked out because Redis is down");
        });

        // Redis back: the cutoff for the device revoked before the outage was written then, and the
        // socket host's list still holds both.
        (await ConnectAsync(beforeToken)).Should().Be(HttpStatusCode.Forbidden);
        (await ConnectAsync(duringToken)).Should().Be(HttpStatusCode.Forbidden);
    }

    [SkippableFact]
    public async Task OUT_REDIS_02_ARemovedMember_LosesAccessWithinTheCacheLifetime()
    {
        Skip.IfNot(E2EConfig.OutageTestsEnabled, OutageControl.SkipReason);
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        // An owner, and a member of the owner's tenant added to one of its workspaces.
        var owner = await fixture.CreateUserAsync();
        var tenantRef = await fixture.GetTenantRefAsync(owner.Token);
        var workspaceRef = await fixture.CreateWorkspaceAsync(owner.Token);
        var member = await fixture.CreateUserInTenantAsync(owner.Token, tenantRef);
        (await fixture.Auth.As(owner.Token).AddWorkspaceMemberRawAsync(workspaceRef, new AddMemberRequest(Email: member.Email)))
            .IsSuccessStatusCode.Should().BeTrue();
        var memberRef = await fixture.FindTenantMemberRefAsync(owner.Token, tenantRef, member.Email);
        memberRef.Should().NotBeNull();

        // The member holds no permissions there, so a request may be refused for that, but never as
        // an outsider: the management host resolves (and caches) the membership first.
        var endpoint = ServicesFixture.ManagementUrl($"/api/workspaces/{workspaceRef}/registration-policies");
        await ServicesFixture.PollUntilAsync(
            "the member to reach the workspace on the management host",
            async () =>
            {
                var code = await ServicesFixture.ReadErrorCodeAsync(await fixture.SendAsync(HttpMethod.Get, endpoint, member.Token));
                return (code != "WORKSPACE_FORBIDDEN", $"code {code ?? "none"}");
            },
            TimeSpan.FromSeconds(15),
            TimeSpan.FromMilliseconds(500));

        await OutageControl.RunWithRestoreAsync(OutageControl.RedisStart, async () =>
        {
            await OutageControl.RunAsync(OutageControl.RedisStop);

            (await fixture.Auth.As(owner.Token).RemoveWorkspaceMemberRawAsync(workspaceRef, memberRef!.Value)).IsSuccessStatusCode
                .Should().BeTrue("removing a member does not depend on Redis");

            // The auth host decides from its database, so it refuses at once.
            (await fixture.ResolveWorkspaceAsync(member.Token, workspaceRef)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

            // The management host was not told (the announcement goes over Redis), so it may serve
            // what it cached until that entry is older than the short lifetime.
            await ServicesFixture.PollUntilAsync(
                "the management host to refuse the removed member",
                async () =>
                {
                    var code = await ServicesFixture.ReadErrorCodeAsync(await fixture.SendAsync(HttpMethod.Get, endpoint, member.Token));
                    return (code == "WORKSPACE_FORBIDDEN", $"code {code ?? "none"}");
                },
                AccessCacheBound,
                TimeSpan.FromSeconds(1));
        });
    }

    // ── helpers ───────────────────────────────────────────────────────────────────────────

    private static Uri SocketUri => new($"{E2EConfig.SocketWsBaseUrl.TrimEnd('/')}/ws");

    /// <summary>Opens a websocket with the token and closes it again; returns the upgrade's status.</summary>
    private static async Task<HttpStatusCode> ConnectAsync(string deviceToken)
    {
        using var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Authorization", $"Bearer {deviceToken}");
        socket.Options.CollectHttpResponseDetails = true;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        try
        {
            await socket.ConnectAsync(SocketUri, timeout.Token);
        }
        catch (WebSocketException) when (socket.HttpStatusCode != 0)
        {
            return socket.HttpStatusCode;
        }

        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token);
        return HttpStatusCode.SwitchingProtocols;
    }

    /// <summary>Opens a websocket with the token and leaves it open.</summary>
    private static async Task<ClientWebSocket> OpenAsync(string deviceToken)
    {
        var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Authorization", $"Bearer {deviceToken}");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await socket.ConnectAsync(SocketUri, timeout.Token);
        return socket;
    }

    /// <summary>Reads (and discards) what the server sends until it closes the connection.</summary>
    private static async Task WaitForServerCloseAsync(ClientWebSocket socket, string because)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var buffer = new byte[4096];
        try
        {
            while (socket.State == WebSocketState.Open)
            {
                var received = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), timeout.Token);
                if (received.MessageType == WebSocketMessageType.Close)
                {
                    return;
                }
            }
        }
        catch (Exception ex) when (timeout.IsCancellationRequested)
        {
            throw new TimeoutException($"The socket host did not close the connection within 30 s: {because}.", ex);
        }
        catch (WebSocketException)
        {
            // The server dropped the connection rather than closing it: closed all the same.
        }
    }

    private Task<HttpResponseMessage> RevokeAsync(Guid workspace, Guid clientRef, string token) =>
        fixture.SendAsync(HttpMethod.Patch, ServicesFixture.ManagementUrl($"/api/workspaces/{workspace}/clients/{clientRef}"), token, new { Status = 2 });
}
