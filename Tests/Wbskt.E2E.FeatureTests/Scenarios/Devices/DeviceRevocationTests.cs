using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text.Json;
using FluentAssertions;
using Wbskt.E2E.FeatureTests.Fixtures;

namespace Wbskt.E2E.FeatureTests.Scenarios.Devices;

/// <summary>
/// A device that loses access keeps a token that is valid for up to an hour, so it is the socket
/// host that has to refuse it. These connect with a token minted before the device was revoked,
/// deleted or had its secret rotated, and expect the websocket upgrade to be refused with 403.
///
/// The socket host learns of revocations by event and keeps them in memory, so the restart scenario
/// checks the part that has to outlive both: the cutoff the management host records in Redis. It
/// needs <c>E2E_SOCKET_RESTART_COMMAND</c> (see <see cref="E2EConfig.SocketRestartCommand"/>) and
/// skips without it.
///
/// Requires the auth, management and socket hosts running. Skips gracefully when they are down.
/// </summary>
[Collection(E2ECollection.Name)]
public sealed class DeviceRevocationTests(ServicesFixture fixture)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed record SecretDto(Guid ClientRefId, string Secret);

    [SkippableFact]
    public async Task DEV_REV_01_ARevokedDevicesToken_IsRefusedByTheSocketHost()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspace) = await fixture.RegisterAndLoginUserAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspace);
        var (clientRef, secret) = await fixture.RegisterClientAsync(pin, "to-revoke");
        var deviceToken = await fixture.LoginClientAsync(clientRef, secret);

        (await ConnectAsync(deviceToken)).Should().Be(HttpStatusCode.SwitchingProtocols);

        (await RevokeAsync(workspace, clientRef, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await ConnectAsync(deviceToken)).Should().Be(HttpStatusCode.Forbidden);
    }

    [SkippableFact]
    public async Task DEV_REV_02_RevocationsOutliveASocketHostRestart()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        Skip.If(E2EConfig.SocketRestartCommand is null,
            "Restarts the socket host. Set E2E_SOCKET_RESTART_COMMAND to run it.");

        var (token, workspace) = await fixture.RegisterAndLoginUserAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspace);

        var (revokedRef, revokedSecret) = await fixture.RegisterClientAsync(pin, "revoked");
        var (deletedRef, deletedSecret) = await fixture.RegisterClientAsync(pin, "deleted");
        var (rotatedRef, oldSecret) = await fixture.RegisterClientAsync(pin, "rotated");
        var (untouchedRef, untouchedSecret) = await fixture.RegisterClientAsync(pin, "untouched");

        var revokedToken = await fixture.LoginClientAsync(revokedRef, revokedSecret);
        var deletedToken = await fixture.LoginClientAsync(deletedRef, deletedSecret);
        var oldToken = await fixture.LoginClientAsync(rotatedRef, oldSecret);

        // Token issue times have whole seconds, and a token from the rotation's own second is let
        // through (it may be the device signing in with the new secret), so the rotation has to
        // land in a later second than the old token for that token to be refused.
        await Task.Delay(TimeSpan.FromSeconds(1.1));

        (await RevokeAsync(workspace, revokedRef, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Send(HttpMethod.Delete, workspace, $"clients/{deletedRef}", token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var rotation = await Send(HttpMethod.Post, workspace, $"clients/{rotatedRef}/rotate-secret", token);
        rotation.StatusCode.Should().Be(HttpStatusCode.OK);
        var newSecret = (await rotation.Content.ReadFromJsonAsync<SecretDto>(JsonOptions))!.Secret;

        // Everything the socket host heard about these devices is gone with the restart.
        await RestartSocketHostAsync();

        (await ConnectAsync(revokedToken)).Should().Be(HttpStatusCode.Forbidden, "the device was revoked");
        (await ConnectAsync(deletedToken)).Should().Be(HttpStatusCode.Forbidden, "the device was deleted");
        (await ConnectAsync(oldToken)).Should().Be(HttpStatusCode.Forbidden, "the token predates the secret rotation");

        // And the devices that should get in still do.
        (await ConnectAsync(await fixture.LoginClientAsync(rotatedRef, newSecret))).Should().Be(HttpStatusCode.SwitchingProtocols);
        (await ConnectAsync(await fixture.LoginClientAsync(untouchedRef, untouchedSecret))).Should().Be(HttpStatusCode.SwitchingProtocols);
    }

    // ── helpers ───────────────────────────────────────────────────────────────────────────

    /// <summary>Opens a websocket with the token and closes it again; returns the upgrade's status.</summary>
    private static async Task<HttpStatusCode> ConnectAsync(string deviceToken)
    {
        using var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Authorization", $"Bearer {deviceToken}");
        socket.Options.CollectHttpResponseDetails = true;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        try
        {
            await socket.ConnectAsync(new Uri($"{E2EConfig.SocketWsBaseUrl.TrimEnd('/')}/ws"), timeout.Token);
        }
        catch (WebSocketException) when (socket.HttpStatusCode != 0)
        {
            return socket.HttpStatusCode;
        }

        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token);
        return HttpStatusCode.SwitchingProtocols;
    }

    private static async Task RestartSocketHostAsync()
    {
        using var restart = Process.Start(new ProcessStartInfo("bash", ["-c", E2EConfig.SocketRestartCommand!])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true
        })!;
        var output = restart.StandardOutput.ReadToEndAsync();
        var error = restart.StandardError.ReadToEndAsync();
        await restart.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(2));

        restart.ExitCode.Should().Be(0, $"the socket host restart failed:\n{await output}\n{await error}");
    }

    private Task<HttpResponseMessage> RevokeAsync(Guid workspace, Guid clientRef, string token) =>
        Send(HttpMethod.Patch, workspace, $"clients/{clientRef}", token, new { Status = 2 });

    private Task<HttpResponseMessage> Send(HttpMethod method, Guid workspace, string path, string token, object? body = null) =>
        fixture.SendAsync(method, ServicesFixture.ManagementUrl($"/api/workspaces/{workspace}/{path}"), token, body);
}
