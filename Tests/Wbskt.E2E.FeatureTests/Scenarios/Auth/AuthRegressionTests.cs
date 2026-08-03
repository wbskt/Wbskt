using System.Net;
using FluentAssertions;
using Wbskt.E2E.FeatureTests.Fixtures;

namespace Wbskt.E2E.FeatureTests.Scenarios.Auth;

/// <summary>
/// Section 2 of Docs/Auth.Host.E2E.Scenarios.md — regression coverage for four defects in the auth
/// host's API surface. Every test here fails against the revision that preceded the fix, which is
/// the only property that makes a regression test worth keeping.
///
/// Requires the auth and management hosts running, and a database with the corrected stored
/// procedures deployed (REG_07 through REG_10 assert behaviour that lives in SQL). Skips gracefully
/// when the hosts are down.
/// </summary>
[Collection(E2ECollection.Name)]
public sealed class AuthRegressionTests(ServicesFixture fixture)
{
    // ── Fix 1: an unresolvable workspace reference is Forbidden, not NotFound ──────────────
    //
    // The management host resolves every workspace-scoped request through /api/workspaces/resolve
    // and maps only 401 and 403; any other status became a Failure and reached the user as a 500.

    [SkippableFact]
    public async Task REG_01_Resolve_UnknownWorkspaceRef_Returns403()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (adminToken, _) = await fixture.LoginAsAdminAsync();

        var response = await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl("/api/workspaces/resolve"),
            adminToken,
            new { WorkspaceRef = Guid.NewGuid() });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "an unresolvable reference must be indistinguishable from one the caller cannot see");

        (await ServicesFixture.ReadErrorCodeAsync(response)).Should().Be("WORKSPACE_NOT_FOUND");
    }

    [SkippableFact]
    public async Task REG_02_Resolve_DeletedWorkspace_Returns403()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (adminToken, _) = await fixture.LoginAsAdminAsync();
        var workspaceRef = await fixture.CreateWorkspaceAsync(adminToken);

        var deleteResponse = await fixture.SendAsync(
            HttpMethod.Delete, ServicesFixture.AuthUrl($"/api/workspaces/{workspaceRef}"), adminToken);
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var response = await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl("/api/workspaces/resolve"),
            adminToken,
            new { WorkspaceRef = workspaceRef });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [SkippableFact]
    public async Task REG_03_ManagementRoute_DeletedWorkspace_Returns403NotServerError()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (adminToken, _) = await fixture.LoginAsAdminAsync();
        var workspaceRef = await fixture.CreateWorkspaceAsync(adminToken);

        await fixture.SendAsync(
            HttpMethod.Delete, ServicesFixture.AuthUrl($"/api/workspaces/{workspaceRef}"), adminToken);

        // The user-visible half of the defect. Resources in other services outlive a deleted
        // workspace, so a stale reference is the normal state of affairs — and it used to 500.
        var response = await fixture.SendAsync(
            HttpMethod.Get,
            ServicesFixture.ManagementUrl($"/api/workspaces/{workspaceRef}/registration-policies"),
            adminToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "a stale workspace reference is a permission answer, not a server fault");
    }

    [SkippableFact]
    public async Task REG_04_AddMember_UnknownWorkspaceRef_Returns403()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (adminToken, _) = await fixture.LoginAsAdminAsync();

        var response = await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/workspaces/{Guid.NewGuid()}/members"),
            adminToken,
            new { Email = "admin@wbskt.com" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ServicesFixture.ReadErrorCodeAsync(response)).Should().Be("WORKSPACE_NOT_FOUND");
    }

    [SkippableFact]
    public async Task REG_05_GetMembers_UnknownWorkspaceRef_Returns403()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (adminToken, _) = await fixture.LoginAsAdminAsync();

        var response = await fixture.SendAsync(
            HttpMethod.Get,
            ServicesFixture.AuthUrl($"/api/workspaces/{Guid.NewGuid()}/members"),
            adminToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ServicesFixture.ReadErrorCodeAsync(response)).Should().Be("WORKSPACE_NOT_FOUND");
    }

    [SkippableFact]
    public async Task REG_06_UpdateAndDelete_UnknownWorkspaceRef_Returns403()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (adminToken, _) = await fixture.LoginAsAdminAsync();
        var unknownRef = Guid.NewGuid();

        var updateResponse = await fixture.SendAsync(
            HttpMethod.Put,
            ServicesFixture.AuthUrl($"/api/workspaces/{unknownRef}"),
            adminToken,
            new { Name = "renamed", Description = (string?)null });

        updateResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var deleteResponse = await fixture.SendAsync(
            HttpMethod.Delete, ServicesFixture.AuthUrl($"/api/workspaces/{unknownRef}"), adminToken);

        deleteResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Fix 2: an unknown permission slug is rejected, not silently skipped ────────────────
    //
    // The grant procedures skipped the MERGE when the slug did not resolve, so a typo returned 204
    // having written nothing. Requires the corrected procedures to be deployed.

    [SkippableFact]
    public async Task REG_07_GrantRolePermission_UnknownSlug_Returns400()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (adminToken, _) = await fixture.LoginAsAdminAsync();
        var tenantRef = await fixture.GetTenantRefAsync(adminToken);
        var roleRef = await fixture.CreateRoleAsync(adminToken, tenantRef);

        var response = await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/roles/{roleRef}/permissions"),
            adminToken,
            new { Slug = "roles.mange", IsDeny = false });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "a typo'd slug grants nothing, so answering 204 reports a success that did not happen");

        (await ServicesFixture.ReadErrorCodeAsync(response)).Should().Be("AUTH_OPERATION_REJECTED");
    }

    [SkippableFact]
    public async Task REG_08_GrantRolePermission_UnknownSlug_WritesNothing()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (adminToken, _) = await fixture.LoginAsAdminAsync();
        var tenantRef = await fixture.GetTenantRefAsync(adminToken);
        var roleRef = await fixture.CreateRoleAsync(adminToken, tenantRef);

        await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/roles/{roleRef}/permissions"),
            adminToken,
            new { Slug = "roles.mange", IsDeny = false });

        // Proves the 400 is not merely cosmetic — nothing was persisted under the bad slug.
        var slugs = await fixture.GetRolePermissionSlugsAsync(adminToken, tenantRef, roleRef);
        slugs.Should().NotContain("roles.mange");
    }

    [SkippableFact]
    public async Task REG_09_GrantUserPermission_UnknownSlug_Returns400()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (adminToken, _) = await fixture.LoginAsAdminAsync();
        var tenantRef = await fixture.GetTenantRefAsync(adminToken);

        var user = await fixture.CreateUserInTenantAsync(adminToken, tenantRef);
        var userRef = await fixture.FindTenantMemberRefAsync(adminToken, tenantRef, user.Email);
        userRef.Should().NotBeNull("a user who joined by invitation is a member of the inviting tenant, so must be listable");

        var response = await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/members/{userRef}/permissions"),
            adminToken,
            new { Slug = "logs.raed", IsDeny = false, WorkspaceRef = (Guid?)null });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ServicesFixture.ReadErrorCodeAsync(response)).Should().Be("AUTH_OPERATION_REJECTED");
    }

    [SkippableFact]
    public async Task REG_10_GrantRolePermission_ValidSlug_Succeeds()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (adminToken, _) = await fixture.LoginAsAdminAsync();
        var tenantRef = await fixture.GetTenantRefAsync(adminToken);
        var roleRef = await fixture.CreateRoleAsync(adminToken, tenantRef);

        var response = await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/roles/{roleRef}/permissions"),
            adminToken,
            new { Slug = "logs.read", IsDeny = false });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent,
            "tightening the unknown-slug path must not break the valid one");

        var slugs = await fixture.GetRolePermissionSlugsAsync(adminToken, tenantRef, roleRef);
        slugs.Should().Contain("logs.read");
    }

    // ── Fix 3: role grants no longer accept a workspace scope ─────────────────────────────
    //
    // Role permissions are unscoped — the scope is chosen when the role is assigned. The endpoint
    // shared the user-grant record, so it accepted a workspaceRef it could only ignore.

    [SkippableFact]
    public async Task REG_11_GrantRolePermission_WithWorkspaceRef_IsRejected()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (adminToken, workspaceRef) = await fixture.LoginAsAdminAsync();
        var tenantRef = await fixture.GetTenantRefAsync(adminToken);
        var roleRef = await fixture.CreateRoleAsync(adminToken, tenantRef);

        var response = await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/roles/{roleRef}/permissions"),
            adminToken,
            new { Slug = "logs.read", IsDeny = false, WorkspaceRef = workspaceRef });

        // Removing the property alone was not enough: the deserializer drops unmapped members by
        // default, which is the same silent success by another route.
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "a scope that cannot be applied must be refused, not quietly discarded");
    }

    // ── Fix 4: paged endpoints report the total ───────────────────────────────────────────
    //
    // The stored procedures were already computing TotalCount; the controllers discarded it,
    // leaving callers unable to tell a final page from a full one.

    [SkippableFact]
    public async Task REG_12_ListRoles_SetsTotalCountHeader()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (adminToken, _) = await fixture.LoginAsAdminAsync();
        var tenantRef = await fixture.GetTenantRefAsync(adminToken);

        // The seed ships Admin and User; adding one guarantees the total exceeds the page size.
        await fixture.CreateRoleAsync(adminToken, tenantRef);

        var (itemCount, totalCount) = await fixture.GetPageAsync(
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/roles?skip=0&take=1"), adminToken);

        itemCount.Should().Be(1);
        totalCount.Should().NotBeNull("X-Total-Count is what makes skip/take usable");
        totalCount.Should().BeGreaterThan(1, "the total must describe the collection, not the page");
    }

    [SkippableFact]
    public async Task REG_13_AllTenantListEndpoints_SetTotalCountHeader()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (adminToken, _) = await fixture.LoginAsAdminAsync();
        var tenantRef = await fixture.GetTenantRefAsync(adminToken);

        await fixture.CreateGroupAsync(adminToken, tenantRef);

        string[] paths =
        [
            $"/api/tenants/{tenantRef}/roles?take=1",
            $"/api/tenants/{tenantRef}/groups?take=1",
            $"/api/tenants/{tenantRef}/permissions?take=1",
            $"/api/tenants/{tenantRef}/members?take=1"
        ];

        foreach (var path in paths)
        {
            var (_, totalCount) = await fixture.GetPageAsync(ServicesFixture.AuthUrl(path), adminToken);
            totalCount.Should().NotBeNull($"{path} is a paged endpoint and must report its total");
        }
    }

    [SkippableFact]
    public async Task REG_14_ListWorkspaceMembers_SetsTotalCountHeader()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (adminToken, workspaceRef) = await fixture.LoginAsAdminAsync();

        var (itemCount, totalCount) = await fixture.GetPageAsync(
            ServicesFixture.AuthUrl($"/api/workspaces/{workspaceRef}/members?take=1"), adminToken);

        itemCount.Should().BeLessThanOrEqualTo(1);
        totalCount.Should().NotBeNull();
        totalCount.Should().BeGreaterThan(0, "the workspace has at least its owner");
    }

    [SkippableFact]
    public async Task REG_15_TotalCount_IsInvariantAcrossPages()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (adminToken, _) = await fixture.LoginAsAdminAsync();
        var tenantRef = await fixture.GetTenantRefAsync(adminToken);

        // The permission catalogue is seeded and static, so it pages predictably.
        var (_, firstPageTotal) = await fixture.GetPageAsync(
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/permissions?skip=0&take=1"), adminToken);

        var (_, secondPageTotal) = await fixture.GetPageAsync(
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/permissions?skip=1&take=1"), adminToken);

        firstPageTotal.Should().NotBeNull();
        secondPageTotal.Should().Be(firstPageTotal, "the total describes the collection, not the window");
    }
}
