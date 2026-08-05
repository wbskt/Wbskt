using System.Net;
using FluentAssertions;
using Wbskt.E2E.FeatureTests.Fixtures;

namespace Wbskt.E2E.FeatureTests.Scenarios.Auth;

/// <summary>
/// Section 10 of Docs/Auth.Host.E2E.Scenarios.md — creating, listing, renaming and deleting
/// workspaces.
///
/// The scenario worth singling out is WS_CRD_04: creating a workspace makes you an administrator
/// *of that workspace*, never of the tenant. Since tenant administration requires its permissions
/// held tenant-wide and a workspace grant is scoped, that boundary is what stops a member from
/// escalating out of a workspace they were given.
///
/// Requires the auth host running. Skips gracefully when it is down.
/// </summary>
[Collection(E2ECollection.Name)]
public sealed class WorkspaceLifecycleTests(ServicesFixture fixture)
{
    // ── Creating ──────────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task WS_CRD_01_CreateWorkspace_ReturnsReference()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var user = await fixture.CreateUserAsync();

        var response = await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl("/api/workspaces"),
            user.Token,
            new { Name = $"e2e-{Guid.NewGuid():N}", Description = "created by a scenario" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [SkippableFact]
    public async Task WS_CRD_02_CreatedWorkspace_AppearsInTheCallersList()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var user = await fixture.CreateUserAsync();
        var workspaceRef = await fixture.CreateWorkspaceAsync(user.Token);

        (await fixture.GetWorkspaceRefsAsync(user.Token)).Should().Contain(workspaceRef);
    }

    [SkippableFact]
    public async Task WS_CRD_03_Creator_IsAdministratorOfTheWorkspace()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var user = await fixture.CreateUserAsync();
        var workspaceRef = await fixture.CreateWorkspaceAsync(user.Token);

        (await fixture.GetEffectivePermissionsAsync(user.Token, workspaceRef))
            .Should().Contain(["users.manage", "roles.manage"]);
    }

    [SkippableFact]
    public async Task WS_CRD_04_WorkspaceAdmin_IsNotATenantAdmin()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var owner = await fixture.CreateUserAsync();
        var tenantRef = await fixture.GetTenantRefAsync(owner.Token);

        // Invited in, so they are a member of someone else's tenant rather than an admin of their
        // own. Creating a workspace there hands them Admin *scoped to that workspace*.
        var member = await fixture.CreateUserInTenantAsync(owner.Token, tenantRef);
        var workspaceRef = await fixture.CreateWorkspaceAsync(member.Token);

        (await fixture.GetEffectivePermissionsAsync(member.Token, workspaceRef))
            .Should().Contain("roles.manage", "they administer this workspace");

        // Tenant administration requires the slug tenant-wide, which a workspace-scoped grant
        // deliberately does not satisfy. This is the escalation boundary.
        var tenantRoles = await fixture.SendAsync(
            HttpMethod.Get, ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/roles"), member.Token);

        tenantRoles.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "a workspace-scoped grant must never reach tenant administration");
        (await ServicesFixture.ReadErrorCodeAsync(tenantRoles)).Should().Be("PERMISSION_UNAUTHORIZED");
    }

    [SkippableFact]
    public async Task WS_CRD_05_CreateWithNullDescription_Succeeds()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var user = await fixture.CreateUserAsync();

        var response = await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl("/api/workspaces"),
            user.Token,
            new { Name = $"e2e-{Guid.NewGuid():N}", Description = (string?)null });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [SkippableFact]
    public async Task WS_CRD_06_EmptyName_IsRejectedByValidation()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var user = await fixture.CreateUserAsync();

        var response = await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl("/api/workspaces"),
            user.Token,
            new { Name = string.Empty, Description = (string?)null });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [SkippableFact]
    public async Task WS_CRD_07_OverlongName_IsRejectedByValidation()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var user = await fixture.CreateUserAsync();

        // Lengths mirror dbo.Workspaces so over-long input fails here rather than truncating in SQL.
        var response = await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl("/api/workspaces"),
            user.Token,
            new { Name = new string('x', 101), Description = (string?)null });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [SkippableFact]
    public async Task WS_CRD_08_OverlongDescription_IsRejectedByValidation()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var user = await fixture.CreateUserAsync();

        var response = await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl("/api/workspaces"),
            user.Token,
            new { Name = "valid", Description = new string('x', 501) });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [SkippableFact]
    public async Task WS_CRD_09_WorkspaceNamesNeedNotBeUnique()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var user = await fixture.CreateUserAsync();
        var name = $"e2e-duplicate-{Guid.NewGuid():N}";

        var first = await fixture.CreateWorkspaceAsync(user.Token, name);
        var second = await fixture.CreateWorkspaceAsync(user.Token, name);

        // No uniqueness constraint exists; pinning it so adding one is a deliberate decision.
        second.Should().NotBe(first);
    }

    [SkippableFact]
    public async Task WS_CRD_10_CreateWithoutToken_Returns401()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var response = await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl("/api/workspaces"),
            body: new { Name = "unauthenticated", Description = (string?)null });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Listing ───────────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task WS_CRD_11_ListShowsOnlyTheCallersWorkspaces()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var userA = await fixture.CreateUserAsync();
        var workspaceA = await fixture.CreateWorkspaceAsync(userA.Token);

        var userB = await fixture.CreateUserAsync();
        var workspaceB = await fixture.CreateWorkspaceAsync(userB.Token);

        var visibleToA = await fixture.GetWorkspaceRefsAsync(userA.Token);

        visibleToA.Should().Contain(workspaceA);
        visibleToA.Should().NotContain(workspaceB);
    }

    [SkippableFact]
    public async Task WS_CRD_12_RegistrationYieldsExactlyOneWorkspace()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var user = await fixture.CreateUserAsync();

        // Registration provisions a tenant and one default workspace in the same transaction, so a
        // brand-new account is never workspace-less and the console has somewhere to land.
        (await fixture.GetWorkspaceRefsAsync(user.Token)).Should().HaveCount(1);
    }

    // ── Updating ──────────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task WS_CRD_13_Update_RenamesTheWorkspace()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var user = await fixture.CreateUserAsync();
        var workspaceRef = await fixture.CreateWorkspaceAsync(user.Token);

        var response = await fixture.SendAsync(
            HttpMethod.Put,
            ServicesFixture.AuthUrl($"/api/workspaces/{workspaceRef}"),
            user.Token,
            new { Name = "renamed by a scenario", Description = "and re-described" });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [SkippableFact]
    public async Task WS_CRD_14_UpdateByANonMember_Returns403()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var owner = await fixture.CreateUserAsync();
        var workspaceRef = await fixture.CreateWorkspaceAsync(owner.Token);

        var outsider = await fixture.CreateUserAsync();

        var response = await fixture.SendAsync(
            HttpMethod.Put,
            ServicesFixture.AuthUrl($"/api/workspaces/{workspaceRef}"),
            outsider.Token,
            new { Name = "hijacked", Description = (string?)null });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [SkippableFact]
    public async Task WS_CRD_15_UpdateByAMemberWithoutUsersManage_Returns403()
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

        // A member, but holding nothing — membership is not authority.
        var response = await fixture.SendAsync(
            HttpMethod.Put,
            ServicesFixture.AuthUrl($"/api/workspaces/{workspaceRef}"),
            member.Token,
            new { Name = "renamed by a member", Description = (string?)null });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ServicesFixture.ReadErrorCodeAsync(response)).Should().Be("PERMISSION_UNAUTHORIZED");
    }

    [SkippableFact]
    public async Task WS_CRD_16_UpdateWithEmptyName_IsRejectedByValidation()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var user = await fixture.CreateUserAsync();
        var workspaceRef = await fixture.CreateWorkspaceAsync(user.Token);

        var response = await fixture.SendAsync(
            HttpMethod.Put,
            ServicesFixture.AuthUrl($"/api/workspaces/{workspaceRef}"),
            user.Token,
            new { Name = string.Empty, Description = (string?)null });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Deleting ──────────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task WS_CRD_17_Delete_RemovesItFromTheList()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var user = await fixture.CreateUserAsync();
        var workspaceRef = await fixture.CreateWorkspaceAsync(user.Token);

        var response = await fixture.SendAsync(
            HttpMethod.Delete, ServicesFixture.AuthUrl($"/api/workspaces/{workspaceRef}"), user.Token);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await fixture.GetWorkspaceRefsAsync(user.Token)).Should().NotContain(workspaceRef);
    }

    [SkippableFact]
    public async Task WS_CRD_18_DeleteByAMemberWithoutUsersManage_Returns403()
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

        var response = await fixture.SendAsync(
            HttpMethod.Delete, ServicesFixture.AuthUrl($"/api/workspaces/{workspaceRef}"), member.Token);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [SkippableFact]
    public async Task WS_CRD_19_DeletingTwice_Returns403TheSecondTime()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var user = await fixture.CreateUserAsync();
        var workspaceRef = await fixture.CreateWorkspaceAsync(user.Token);

        await fixture.SendAsync(
            HttpMethod.Delete, ServicesFixture.AuthUrl($"/api/workspaces/{workspaceRef}"), user.Token);

        var second = await fixture.SendAsync(
            HttpMethod.Delete, ServicesFixture.AuthUrl($"/api/workspaces/{workspaceRef}"), user.Token);

        // The reference stops resolving, which reads the same as one that never existed.
        second.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ServicesFixture.ReadErrorCodeAsync(second)).Should().Be("WORKSPACE_NOT_FOUND");
    }

    [SkippableFact]
    public async Task WS_CRD_20_DeleteRemovesItForEveryMember()
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

        (await fixture.GetWorkspaceRefsAsync(member.Token)).Should().Contain(workspaceRef);

        await fixture.SendAsync(
            HttpMethod.Delete, ServicesFixture.AuthUrl($"/api/workspaces/{workspaceRef}"), owner.Token);

        (await fixture.GetWorkspaceRefsAsync(member.Token)).Should().NotContain(workspaceRef);
    }

    [SkippableFact]
    public async Task WS_CRD_21_DeletedWorkspace_NoLongerResolves()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var user = await fixture.CreateUserAsync();
        var workspaceRef = await fixture.CreateWorkspaceAsync(user.Token);

        await fixture.SendAsync(
            HttpMethod.Delete, ServicesFixture.AuthUrl($"/api/workspaces/{workspaceRef}"), user.Token);

        // Resources in other services outlive the workspace, so their references land here. A 403
        // is what keeps that a permission answer rather than a 500 in the management host.
        var response = await fixture.ResolveWorkspaceAsync(user.Token, workspaceRef);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
