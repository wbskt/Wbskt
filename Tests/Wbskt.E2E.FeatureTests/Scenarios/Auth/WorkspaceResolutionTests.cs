using System.Net;
using FluentAssertions;
using Wbskt.E2E.FeatureTests.Fixtures;

namespace Wbskt.E2E.FeatureTests.Scenarios.Auth;

/// <summary>
/// Section 9 of Docs/Auth.Host.E2E.Scenarios.md — <c>POST /api/workspaces/resolve</c>.
///
/// This is the gate every other host depends on: the management host calls it before every
/// workspace-scoped request, turning a public reference into an internal ID plus the caller's
/// effective permission slugs. Its three-way answer is load-bearing — 401 means "cannot identify
/// you", 403 covers both "not a member" and "no such workspace", and anything else the management
/// host reads as a server fault. Getting that wrong surfaces to users as a 500.
///
/// Requires the auth host running. Skips gracefully when it is down.
/// </summary>
[Collection(E2ECollection.Name)]
public sealed class WorkspaceResolutionTests(ServicesFixture fixture)
{
    [SkippableFact]
    public async Task WS_RES_01_OwnWorkspace_ResolvesWithPermissions()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (adminToken, workspaceRef) = await fixture.LoginAsAdminAsync();

        var response = await fixture.ResolveWorkspaceAsync(adminToken, workspaceRef);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var resolved = await ServicesFixture.ReadResolvedWorkspaceAsync(response);
        resolved.WorkspaceId.Should().BeGreaterThan(0);
        resolved.Permissions.Should().NotBeEmpty();
    }

    [SkippableFact]
    public async Task WS_RES_02_AdminResolve_CarriesTheFullEffectiveSet()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (adminToken, workspaceRef) = await fixture.LoginAsAdminAsync();

        var permissions = await fixture.GetEffectivePermissionsAsync(adminToken, workspaceRef);

        // The slugs other hosts gate on, not just the ones this host uses.
        permissions.Should().Contain(["users.manage", "roles.manage", "clients.read", "workflows.read"]);
    }

    [SkippableFact]
    public async Task WS_RES_04_WorkspaceCreator_ResolvesTheirNewWorkspace()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var user = await fixture.CreateUserAsync();
        var workspaceRef = await fixture.CreateWorkspaceAsync(user.Token);

        var permissions = await fixture.GetEffectivePermissionsAsync(user.Token, workspaceRef);

        // Workspace_Create grants the creator the tenant's Admin role scoped to the new workspace.
        permissions.Should().Contain("users.manage");
    }

    [SkippableFact]
    public async Task WS_RES_03_MemberWithNoRoles_ResolvesWithAnEmptySet()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var owner = await fixture.CreateUserAsync();
        var tenantRef = await fixture.GetTenantRefAsync(owner.Token);
        var workspaceRef = await fixture.CreateWorkspaceAsync(owner.Token);

        // A tenant member with no role, added to the workspace: past the membership gate, holding
        // nothing. An empty permission set is a successful resolution, not a failure — the two
        // gates are separate and the management host applies the slug check itself.
        var member = await fixture.CreateUserInTenantAsync(owner.Token, tenantRef);
        var add = await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/workspaces/{workspaceRef}/members"),
            owner.Token,
            new { Email = member.Email });
        add.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var response = await fixture.ResolveWorkspaceAsync(member.Token, workspaceRef);

        response.StatusCode.Should().Be(HttpStatusCode.OK, "membership alone is enough to resolve");
        (await ServicesFixture.ReadResolvedWorkspaceAsync(response)).Permissions.Should().BeEmpty();
    }

    [SkippableFact]
    public async Task WS_RES_05_UnknownWorkspaceRef_Returns403()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var user = await fixture.CreateUserAsync();

        var response = await fixture.ResolveWorkspaceAsync(user.Token, Guid.NewGuid());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ServicesFixture.ReadErrorCodeAsync(response)).Should().Be("WORKSPACE_NOT_FOUND");
    }

    [SkippableFact]
    public async Task WS_RES_06_WorkspaceOfAnotherUser_Returns403()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var owner = await fixture.CreateUserAsync();
        var workspaceRef = await fixture.CreateWorkspaceAsync(owner.Token);

        var outsider = await fixture.CreateUserAsync();

        var response = await fixture.ResolveWorkspaceAsync(outsider.Token, workspaceRef);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [SkippableFact]
    public async Task WS_RES_06b_NonMemberAndUnknownRef_AreIndistinguishable()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var owner = await fixture.CreateUserAsync();
        var realButForeign = await fixture.CreateWorkspaceAsync(owner.Token);

        var outsider = await fixture.CreateUserAsync();

        var foreign = await fixture.ResolveWorkspaceAsync(outsider.Token, realButForeign);
        var nonexistent = await fixture.ResolveWorkspaceAsync(outsider.Token, Guid.NewGuid());

        // If these differed, the endpoint would confirm which references name a real workspace.
        foreign.StatusCode.Should().Be(nonexistent.StatusCode);
        foreign.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [SkippableFact]
    public async Task WS_RES_07_Unauthenticated_Returns401NotForbidden()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (adminToken, workspaceRef) = await fixture.LoginAsAdminAsync();
        _ = adminToken;

        var response = await fixture.ResolveWorkspaceAsync(token: null, workspaceRef);

        // Must stay distinguishable from 403: the management host maps 401 to "not authenticated"
        // and 403 to "denied", and a console that redirects to login on 401 would otherwise sign
        // the user out over a missing permission.
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [SkippableFact]
    public async Task WS_RES_08_MalformedWorkspaceRef_IsRejectedByValidation()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var user = await fixture.CreateUserAsync();

        var response = await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl("/api/workspaces/resolve"),
            user.Token,
            new { WorkspaceRef = "not-a-guid" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [SkippableFact]
    public async Task WS_RES_09_MissingBody_IsRejectedByValidation()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var user = await fixture.CreateUserAsync();

        var response = await fixture.SendAsync(
            HttpMethod.Post, ServicesFixture.AuthUrl("/api/workspaces/resolve"), user.Token);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [SkippableFact]
    public async Task WS_RES_10_GrantIsVisibleImmediately()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var owner = await fixture.CreateUserAsync();
        var tenantRef = await fixture.GetTenantRefAsync(owner.Token);
        var workspaceRef = await fixture.CreateWorkspaceAsync(owner.Token);

        var member = await fixture.CreateUserInTenantAsync(owner.Token, tenantRef);
        await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/workspaces/{workspaceRef}/members"),
            owner.Token,
            new { Email = member.Email });

        var memberRef = await fixture.FindTenantMemberRefAsync(owner.Token, tenantRef, member.Email);
        memberRef.Should().NotBeNull();

        (await fixture.GetEffectivePermissionsAsync(member.Token, workspaceRef))
            .Should().NotContain("logs.read");

        var grant = await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/members/{memberRef}/permissions"),
            owner.Token,
            new { Slug = "logs.read", IsDeny = false, WorkspaceRef = (Guid?)null });
        grant.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // No caching anywhere on this path, so the next resolve must already reflect it.
        (await fixture.GetEffectivePermissionsAsync(member.Token, workspaceRef))
            .Should().Contain("logs.read");
    }

    [SkippableFact]
    public async Task WS_RES_11_RevocationIsVisibleImmediately()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var owner = await fixture.CreateUserAsync();
        var tenantRef = await fixture.GetTenantRefAsync(owner.Token);
        var workspaceRef = await fixture.CreateWorkspaceAsync(owner.Token);

        var member = await fixture.CreateUserInTenantAsync(owner.Token, tenantRef);
        await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/workspaces/{workspaceRef}/members"),
            owner.Token,
            new { Email = member.Email });

        var memberRef = await fixture.FindTenantMemberRefAsync(owner.Token, tenantRef, member.Email);

        await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/members/{memberRef}/permissions"),
            owner.Token,
            new { Slug = "logs.read", IsDeny = false, WorkspaceRef = (Guid?)null });

        var remove = await fixture.SendAsync(
            HttpMethod.Delete,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/members/{memberRef}/permissions/logs.read"),
            owner.Token);
        remove.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await fixture.GetEffectivePermissionsAsync(member.Token, workspaceRef))
            .Should().NotContain("logs.read");
    }

    [SkippableFact]
    public async Task WS_RES_12_UserLevelDeny_BeatsARoleGrant()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var owner = await fixture.CreateUserAsync();
        var tenantRef = await fixture.GetTenantRefAsync(owner.Token);
        var workspaceRef = await fixture.CreateWorkspaceAsync(owner.Token);

        // The owner holds Admin scoped to this workspace, so logs.read arrives via a role.
        (await fixture.GetEffectivePermissionsAsync(owner.Token, workspaceRef))
            .Should().Contain("logs.read");

        var ownerRef = await fixture.FindTenantMemberRefAsync(owner.Token, tenantRef, owner.Email);
        ownerRef.Should().NotBeNull();

        var deny = await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/members/{ownerRef}/permissions"),
            owner.Token,
            new { Slug = "logs.read", IsDeny = true, WorkspaceRef = (Guid?)null });
        deny.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // User-Deny > User-Allow > Role-Deny > Role-Allow, per Permission_EffectiveSet.
        (await fixture.GetEffectivePermissionsAsync(owner.Token, workspaceRef))
            .Should().NotContain("logs.read");
    }

    [SkippableFact]
    public async Task WS_RES_13_ResponseExposesNoUserIdentifiers()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (adminToken, workspaceRef) = await fixture.LoginAsAdminAsync();

        var response = await fixture.ResolveWorkspaceAsync(adminToken, workspaceRef);
        var body = await response.Content.ReadAsStringAsync();

        // workspaceId is a documented internal, and the slugs are public constants. Nothing else
        // belongs in a response that crosses a service boundary on every request.
        body.Should().NotContain("userId");
        body.Should().NotContain("email");
    }
}
