using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Wbskt.E2E.FeatureTests.Fixtures;

namespace Wbskt.E2E.FeatureTests.Scenarios.Auth;

/// <summary>
/// Section 6a of Docs/Auth.Host.E2E.Scenarios.md — changing a password while signed in, the
/// session list, and per-account lockout.
///
/// Requires the auth host running. Skips gracefully when it is down.
/// </summary>
[Collection(E2ECollection.Name)]
public sealed class AccountSecurityTests(ServicesFixture fixture)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed record SessionDto(int Id, DateTime CreatedAt, DateTime ExpiresAt, string? CreatedByIp);

    // ── Change password ───────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task AUTH_PW_01_ChangingThePassword_EndsOtherSessions_AndKeepsThisOne()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var user = await fixture.CreateUserAsync();
        var elsewhere = await fixture.LoginAsync(user.Email, user.Password);
        var here = await fixture.LoginAsync(user.Email, user.Password);

        // Access-token revocation is by issue time in whole seconds; step past the second these
        // were issued in so the old access token is unambiguously older than the revocation.
        await Task.Delay(TimeSpan.FromMilliseconds(1100));

        const string newPassword = "a brand new passphrase";
        var response = await ChangePasswordAsync(here.AccessToken, user.Password, newPassword);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var fresh = await ServicesFixture.ReadSessionAsync(response);

        (await fixture.RefreshAsync(elsewhere.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await fixture.RefreshAsync(here.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await fixture.SendAsync(HttpMethod.Get, Sessions, here.AccessToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        (await fixture.SendAsync(HttpMethod.Get, Sessions, fresh.AccessToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await fixture.RefreshAsync(fresh.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await fixture.LoginRawAsync(user.Email, user.Password)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await fixture.LoginRawAsync(user.Email, newPassword)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [SkippableFact]
    public async Task AUTH_PW_02_AWrongCurrentPassword_Is400_AndChangesNothing()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var user = await fixture.CreateUserAsync();
        var session = await fixture.LoginAsync(user.Email, user.Password);

        var response = await ChangePasswordAsync(session.AccessToken, "not-the-password", "a brand new passphrase");

        // Not 401: the session is fine, and a client would sign the user out on one.
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ServicesFixture.ReadErrorCodeAsync(response)).Should().Be("AUTH_CURRENT_PASSWORD_INVALID");
        (await fixture.RefreshAsync(session.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await fixture.LoginRawAsync(user.Email, user.Password)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [SkippableFact]
    public async Task AUTH_PW_03_ChangePassword_RequiresAToken()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var response = await ChangePasswordAsync(null, "whatever-it-was", "a brand new passphrase");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Sessions ──────────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task AUTH_SES_01_EachSignIn_IsListed_AndOneCanBeEnded()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var user = await fixture.CreateUserAsync();
        var first = await fixture.LoginAsync(user.Email, user.Password);
        var second = await fixture.LoginAsync(user.Email, user.Password);

        var before = await ListSessionsAsync(second.AccessToken);
        // CreateUserAsync signs in once too, so there are at least the two made here.
        before.Should().HaveCountGreaterThanOrEqualTo(2);

        // Newest first by id: the second sign-in, then the first. End the first.
        var target = before.OrderByDescending(s => s.Id).Skip(1).First();
        (await fixture.SendAsync(HttpMethod.Delete, $"{Sessions}/{target.Id}", second.AccessToken))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var after = await ListSessionsAsync(second.AccessToken);
        after.Select(s => s.Id).Should().NotContain(target.Id);
        after.Should().HaveCount(before.Count - 1);
        (await fixture.RefreshAsync(first.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await fixture.RefreshAsync(second.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [SkippableFact]
    public async Task AUTH_SES_02_AnotherUsersSession_IsNotFound()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var owner = await fixture.CreateUserAsync();
        var stranger = await fixture.CreateUserAsync();
        var ownerSessions = await ListSessionsAsync(owner.Token);
        var target = ownerSessions.First();

        var response = await fixture.SendAsync(HttpMethod.Delete, $"{Sessions}/{target.Id}", stranger.Token);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ListSessionsAsync(owner.Token)).Select(s => s.Id).Should().Contain(target.Id);
    }

    // ── Lockout ───────────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task AUTH_LOCK_01_TenWrongPasswords_LockTheAccount_WithTheOrdinaryAnswer()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var user = await fixture.CreateUserAsync();
        for (var i = 0; i < 10; i++)
        {
            (await fixture.LoginRawAsync(user.Email, "wrong-password")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        var locked = await fixture.LoginRawAsync(user.Email, user.Password);

        // Indistinguishable from a wrong password, so a lock does not confirm the address exists.
        locked.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ServicesFixture.ReadErrorCodeAsync(locked)).Should().Be("AUTH_INVALID_CREDENTIALS");

        // Sessions issued before the lock are untouched by it.
        (await fixture.SendAsync(HttpMethod.Get, Sessions, user.Token)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [SkippableFact]
    public async Task AUTH_LOCK_02_ASuccessfulSignIn_ResetsTheCount()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var user = await fixture.CreateUserAsync();
        for (var i = 0; i < 9; i++)
        {
            await fixture.LoginRawAsync(user.Email, "wrong-password");
        }

        (await fixture.LoginRawAsync(user.Email, user.Password)).StatusCode.Should().Be(HttpStatusCode.OK);
        await fixture.LoginRawAsync(user.Email, "wrong-password");

        (await fixture.LoginRawAsync(user.Email, user.Password)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── helpers ───────────────────────────────────────────────────────────────────────────

    private static string Sessions => ServicesFixture.AuthUrl("/api/auth/sessions");

    private Task<HttpResponseMessage> ChangePasswordAsync(string? accessToken, string current, string next) =>
        fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl("/api/auth/change-password"),
            accessToken,
            new { CurrentPassword = current, NewPassword = next });

    private async Task<IReadOnlyList<SessionDto>> ListSessionsAsync(string accessToken)
    {
        var response = await fixture.SendAsync(HttpMethod.Get, Sessions, accessToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<SessionDto>>(JsonOptions) ?? [];
    }
}
