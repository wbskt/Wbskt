using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Wbskt.E2E.FeatureTests.Fixtures;

namespace Wbskt.E2E.FeatureTests.Scenarios.Auth;

/// <summary>
/// Section 11a of Docs/Auth.Host.E2E.Scenarios.md — invitations that name workspaces, transferring a
/// workspace's ownership, and the reads the default User role carries.
///
/// Requires the auth host running. Skips gracefully when it is down.
/// </summary>
[Collection(E2ECollection.Name)]
public sealed class WorkspaceAccessTests(ServicesFixture fixture)
{
    private sealed record CreatedInvitationDto(Guid RefId, string Email, DateTime ExpiresAt, string Token);

    // ── Invitations that name workspaces ──────────────────────────────────────────────────

    [SkippableFact]
    public async Task INV_WS_01_AnInviteeJoinsTheNamedWorkspaces_AndNoOthers()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var admin = await fixture.CreateUserAsync();
        var tenantRef = await fixture.GetTenantRefAsync(admin.Token);
        var named = await fixture.CreateWorkspaceAsync(admin.Token);
        var other = await fixture.CreateWorkspaceAsync(admin.Token);

        var invitee = await RegisterWithInvitationAsync(admin.Token, tenantRef, new { WorkspaceRefs = new[] { named } });

        (await fixture.ResolveWorkspaceAsync(invitee.AccessToken, named)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await fixture.ResolveWorkspaceAsync(invitee.AccessToken, other)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [SkippableFact]
    public async Task INV_WS_02_AWorkspaceFromAnotherTenant_ReadsAsNotFound()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var admin = await fixture.CreateUserAsync();
        var tenantRef = await fixture.GetTenantRefAsync(admin.Token);
        var stranger = await fixture.CreateUserAsync();
        var foreign = await fixture.CreateWorkspaceAsync(stranger.Token);

        var response = await InviteAsync(admin.Token, tenantRef, ServicesFixture.NewCredentials().Email, new { WorkspaceRefs = new[] { foreign } });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ServicesFixture.ReadErrorCodeAsync(response)).Should().Be("WORKSPACE_NOT_FOUND");
    }

    [SkippableFact]
    public async Task INV_WS_03_TheUserRole_GrantsReadsInTheNamedWorkspace()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var admin = await fixture.CreateUserAsync();
        var tenantRef = await fixture.GetTenantRefAsync(admin.Token);
        var workspace = await fixture.CreateWorkspaceAsync(admin.Token);
        var userRole = (await fixture.GetRolesAsync(admin.Token, tenantRef)).Single(r => r.Name == "User");

        var invitee = await RegisterWithInvitationAsync(
            admin.Token, tenantRef, new { RoleRef = userRole.RefId, WorkspaceRefs = new[] { workspace } });

        var permissions = await fixture.GetEffectivePermissionsAsync(invitee.AccessToken, workspace);
        permissions.Should().Contain(["clients.read", "policies.read", "templates.read", "workflows.read", "logs.read"]);
        permissions.Should().NotContain(["workflows.create", "users.manage", "clients.manage"]);
    }

    // ── Ownership transfer ────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task WS_OWN_01_TransferringOwnership_LetsThePreviousOwnerBeRemoved()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var owner = await fixture.CreateUserAsync();
        var tenantRef = await fixture.GetTenantRefAsync(owner.Token);
        var workspace = await fixture.CreateWorkspaceAsync(owner.Token);
        var member = await fixture.CreateUserInTenantAsync(owner.Token, tenantRef);
        var memberRef = await fixture.FindTenantMemberRefAsync(owner.Token, tenantRef, member.Email);
        var ownerRef = await fixture.FindTenantMemberRefAsync(owner.Token, tenantRef, owner.Email);

        (await TransferAsync(owner.Token, workspace, memberRef!.Value)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        // The new owner was not in the workspace; owning it put them there.
        (await fixture.ResolveWorkspaceAsync(member.Token, workspace)).StatusCode.Should().Be(HttpStatusCode.OK);

        // The owner guard now protects the new owner, not the old one.
        (await fixture.SendAsync(HttpMethod.Delete, ServicesFixture.AuthUrl($"/api/workspaces/{workspace}/members/{memberRef}"), owner.Token))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await fixture.SendAsync(HttpMethod.Delete, ServicesFixture.AuthUrl($"/api/workspaces/{workspace}/members/{ownerRef}"), owner.Token))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [SkippableFact]
    public async Task WS_OWN_02_TransferringToSomeoneOutsideTheTenant_IsNotFound()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var owner = await fixture.CreateUserAsync();
        var workspace = await fixture.CreateWorkspaceAsync(owner.Token);
        var outsider = await fixture.CreateUserAsync();
        var outsiderTenant = await fixture.GetTenantRefAsync(outsider.Token);
        var outsiderRef = await fixture.FindTenantMemberRefAsync(outsider.Token, outsiderTenant, outsider.Email);

        var response = await TransferAsync(owner.Token, workspace, outsiderRef!.Value);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ServicesFixture.ReadErrorCodeAsync(response)).Should().Be("USER_NOT_FOUND");
        (await fixture.ResolveWorkspaceAsync(outsider.Token, workspace)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [SkippableFact]
    public async Task WS_OWN_03_TransferWithoutUsersManage_Returns403()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var owner = await fixture.CreateUserAsync();
        var tenantRef = await fixture.GetTenantRefAsync(owner.Token);
        var workspace = await fixture.CreateWorkspaceAsync(owner.Token);
        var member = await fixture.CreateUserInTenantAsync(owner.Token, tenantRef);
        await fixture.SendAsync(HttpMethod.Post, ServicesFixture.AuthUrl($"/api/workspaces/{workspace}/members"), owner.Token, new { Email = member.Email });
        var memberRef = await fixture.FindTenantMemberRefAsync(owner.Token, tenantRef, member.Email);

        var response = await TransferAsync(member.Token, workspace, memberRef!.Value);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── helpers ───────────────────────────────────────────────────────────────────────────

    private Task<HttpResponseMessage> TransferAsync(string token, Guid workspaceRef, Guid userRef) =>
        fixture.SendAsync(HttpMethod.Put, ServicesFixture.AuthUrl($"/api/workspaces/{workspaceRef}/owner"), token, new { UserRef = userRef });

    private Task<HttpResponseMessage> InviteAsync(string adminToken, Guid tenantRef, string email, object extra)
    {
        var body = System.Text.Json.JsonSerializer.SerializeToNode(extra)!.AsObject();
        body["Email"] = email;
        return fixture.SendAsync(HttpMethod.Post, ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/invitations"), adminToken, body);
    }

    private async Task<ServicesFixture.Session> RegisterWithInvitationAsync(string adminToken, Guid tenantRef, object extra)
    {
        var (username, email, password) = ServicesFixture.NewCredentials();
        var invited = await InviteAsync(adminToken, tenantRef, email, extra);
        invited.EnsureSuccessStatusCode();
        var invitation = await invited.Content.ReadFromJsonAsync<CreatedInvitationDto>()
            ?? throw new InvalidOperationException("Empty invitation response.");

        (await fixture.RegisterAsync(username, email, password, invitation.Token)).EnsureSuccessStatusCode();
        return await fixture.LoginAsync(email, password);
    }
}
