using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Wbskt.Auth.Models;
using Wbskt.E2E.FeatureTests.Fixtures;

namespace Wbskt.E2E.FeatureTests.Scenarios.Auth;

/// <summary>
/// Section 6c of Docs/Auth.Host.E2E.Scenarios.md — changing your username and email address
/// (issue #151). The confirm and revert links are mailed, and the suite has no inbox, so those are
/// covered as far as refusing a link that was never issued; the unit tests cover the rest.
///
/// Requires the auth host running. Skips gracefully when it is down.
/// </summary>
[Collection(AuthCollection.Name)]
public sealed class AccountProfileTests(ServicesFixture fixture)
{
    private static readonly string AccountUrl = ServicesFixture.AuthUrl("/api/auth/account");

    [SkippableFact]
    public async Task AUTH_PROF_01_Rename_ReturnsATokenWithTheNewName_AndASecondRenameWaits30Days()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var user = await fixture.CreateUserAsync();
        var newName = $"renamed-{Guid.NewGuid():N}"[..20];

        var response = await fixture.SendAsync(HttpMethod.Patch, AccountUrl, user.Token, new UpdateAccountRequest(newName));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var session = await response.Content.ReadFromJsonAsync<LoginResponse>();
        NameClaimOf(session!.AccessToken).Should().Be(newName);
        session.RefreshToken.Should().BeNull();

        var again = await fixture.SendAsync(HttpMethod.Patch, AccountUrl, session.AccessToken, new UpdateAccountRequest(newName + "x"));
        again.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ServicesFixture.ReadErrorCodeAsync(again)).Should().Be("USERNAME_CHANGED_RECENTLY");

        // Sign-in is by email, so a rename never locks anyone out.
        (await fixture.LoginRawAsync(user.Email, user.Password)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [SkippableFact]
    public async Task AUTH_PROF_02_Rename_ToAReservedOrTakenName_IsRefused()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var user = await fixture.CreateUserAsync();
        var other = await fixture.CreateUserAsync();

        var reserved = await fixture.SendAsync(HttpMethod.Patch, AccountUrl, user.Token, new UpdateAccountRequest("Adm1n_"));
        reserved.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ServicesFixture.ReadErrorCodeAsync(reserved)).Should().Be("USERNAME_RESERVED");

        var taken = await fixture.SendAsync(HttpMethod.Patch, AccountUrl, user.Token, new UpdateAccountRequest(other.Username));
        taken.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ServicesFixture.ReadErrorCodeAsync(taken)).Should().Be("USERNAME_TAKEN");

        var nothing = await fixture.SendAsync(HttpMethod.Patch, AccountUrl, user.Token, new UpdateAccountRequest());
        nothing.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [SkippableFact]
    public async Task AUTH_PROF_03_ChangeEmail_NeedsThePassword_AndChangesNothingUntilConfirmed()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var user = await fixture.CreateUserAsync();
        var newEmail = $"e2e-moved-{Guid.NewGuid():N}"[..20] + "@test.local";

        var wrong = await fixture.SendAsync(HttpMethod.Post, $"{AccountUrl}/change-email", user.Token, new ChangeEmailRequest(newEmail, "not-the-password"));
        wrong.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ServicesFixture.ReadErrorCodeAsync(wrong)).Should().Be("AUTH_CURRENT_PASSWORD_INVALID");

        var requested = await fixture.SendAsync(HttpMethod.Post, $"{AccountUrl}/change-email", user.Token, new ChangeEmailRequest(newEmail, user.Password));
        requested.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await fixture.LoginRawAsync(user.Email, user.Password)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await fixture.LoginRawAsync(newEmail, user.Password)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [SkippableFact]
    public async Task AUTH_PROF_04_ChangeEmail_ToAnAddressInUse_IsAGenericConflict()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var user = await fixture.CreateUserAsync();
        var other = await fixture.CreateUserAsync();

        var response = await fixture.SendAsync(HttpMethod.Post, $"{AccountUrl}/change-email", user.Token, new ChangeEmailRequest(other.Email, user.Password));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ServicesFixture.ReadErrorCodeAsync(response)).Should().Be("EMAIL_CHANGE_CONFLICT");
    }

    [SkippableFact]
    public async Task AUTH_PROF_05_ALinkThatWasNeverIssued_IsRefused()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        foreach (var path in new[] { "confirm-email-change", "revert-email-change" })
        {
            var response = await fixture.SendAsync(HttpMethod.Post, $"{AccountUrl}/{path}", body: new EmailChangeTokenRequest("not-a-real-token"));

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest, path);
            (await ServicesFixture.ReadErrorCodeAsync(response)).Should().Be("EMAIL_CHANGE_TOKEN_INVALID", path);
        }
    }

    [SkippableFact]
    public async Task AUTH_PROF_06_SignUp_WithAReservedName_IsRefused()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var response = await fixture.RegisterAsync("Support", $"e2e-{Guid.NewGuid():N}"[..12] + "@test.local", "a long enough password");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ServicesFixture.ReadErrorCodeAsync(response)).Should().Be("USERNAME_RESERVED");
    }

    private static string? NameClaimOf(string jwt)
    {
        var payload = jwt.Split('.')[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
        using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
        return doc.RootElement.TryGetProperty("name", out var name) ? name.GetString() : null;
    }
}
