using System.Net;
using FluentAssertions;
using Wbskt.Auth.Models;
using Wbskt.E2E.FeatureTests.Fixtures;

namespace Wbskt.E2E.FeatureTests.Scenarios.Auth;

/// <summary>
/// Section 6b of Docs/Auth.Host.E2E.Scenarios.md — deleting your own account
/// (<c>POST /api/auth/account/delete</c>, issue #149).
///
/// Every test user is the only member of the tenant it registered with, so the plain success case
/// also covers that tenant and its workspaces going with the account. AUTH_DEL_04 is the refusal
/// that keeps a shared tenant from being left without an administrator.
///
/// Requires the auth host running. Skips gracefully when it is down.
/// </summary>
[Collection(AuthCollection.Name)]
public sealed class AccountDeletionTests(ServicesFixture fixture)
{
    private Task<HttpResponseMessage> DeleteAsync(string? token, string password) =>
        token is null
            ? fixture.SendAsync(HttpMethod.Post, ServicesFixture.AuthUrl("/api/auth/account/delete"), body: new DeleteAccountRequest(password))
            : fixture.Auth.As(token).DeleteAccountRawAsync(new DeleteAccountRequest(password));

    [SkippableFact]
    public async Task AUTH_DEL_01_DeleteAccount_EndsSessions_AndFreesTheAddress()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var user = await fixture.CreateUserAsync();
        var session = await fixture.LoginAsync(user.Email, user.Password);

        (await DeleteAsync(user.Token, user.Password)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await fixture.Auth.As(user.Token).GetSessionsRawAsync()).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await fixture.Auth.As(session.AccessToken).GetSessionsRawAsync()).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await fixture.RefreshAsync(session.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await fixture.LoginRawAsync(user.Email, user.Password)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // The address and the username were released, so the same person can start again.
        (await fixture.RegisterAsync(user.Username, user.Email, user.Password)).IsSuccessStatusCode.Should().BeTrue();
    }

    [SkippableFact]
    public async Task AUTH_DEL_02_DeleteAccount_WrongPassword_KeepsTheAccount()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var user = await fixture.CreateUserAsync();

        var response = await DeleteAsync(user.Token, "not-the-password");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ServicesFixture.ReadErrorCodeAsync(response)).Should().Be("AUTH_CURRENT_PASSWORD_INVALID");
        (await fixture.LoginRawAsync(user.Email, user.Password)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [SkippableFact]
    public async Task AUTH_DEL_03_DeleteAccount_RequiresAToken()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        (await DeleteAsync(null, "whatever-it-was")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [SkippableFact]
    public async Task AUTH_DEL_04_DeleteAccount_OnlyAdministratorOfASharedTenant_IsRefused()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var admin = await fixture.CreateUserAsync();
        var tenantRef = await fixture.GetTenantRefAsync(admin.Token);
        await fixture.CreateUserInTenantAsync(admin.Token, tenantRef);

        var response = await DeleteAsync(admin.Token, admin.Password);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ServicesFixture.ReadErrorCodeAsync(response)).Should().Be("ACCOUNT_SOLE_ADMINISTRATOR");
        (await fixture.LoginRawAsync(admin.Email, admin.Password)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [SkippableFact]
    public async Task AUTH_DEL_05_DeleteAccount_Member_LeavesTheSharedTenant()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var admin = await fixture.CreateUserAsync();
        var tenantRef = await fixture.GetTenantRefAsync(admin.Token);
        var member = await fixture.CreateUserInTenantAsync(admin.Token, tenantRef);
        (await fixture.FindTenantMemberRefAsync(admin.Token, tenantRef, member.Email)).Should().NotBeNull();

        (await DeleteAsync(member.Token, member.Password)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var members = await fixture.Auth.As(admin.Token).GetTenantMembersAsync(tenantRef, limit: 200);
        members.Items.Should().ContainSingle("only the administrator is left")
            .Which.Email.Should().BeEquivalentTo(admin.Email);
    }
}
