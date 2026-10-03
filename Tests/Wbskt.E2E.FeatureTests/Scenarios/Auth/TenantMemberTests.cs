using System.Net;
using FluentAssertions;
using Wbskt.E2E.FeatureTests.Fixtures;

namespace Wbskt.E2E.FeatureTests.Scenarios.Auth;

/// <summary>
/// Section 13 of Docs/Auth.Host.E2E.Scenarios.md — administering the people in a tenant: listing
/// them, reading back what they hold, and assigning roles, groups and direct permissions.
///
/// Member *removal* is covered by TenantLifecycleTests (`TEN_13`–`TEN_16`), which owns offboarding
/// and its workspace-transfer rules; this file does not repeat it.
///
/// The scenarios that carry the most weight are MEM_25 and MEM_28: a direct user-level entry
/// outranks anything role-derived in both directions, and removing one is a delete rather than a
/// deny — without that distinction an accidental grant could never be undone.
///
/// Requires the auth host running. Skips gracefully when it is down.
/// </summary>
[Collection(E2ECollection.Name)]
public sealed class TenantMemberTests(ServicesFixture fixture)
{
    private async Task<(ServicesFixture.TestUser Admin, Guid TenantRef)> ArrangeAsync()
    {
        var admin = await fixture.CreateUserAsync();
        return (admin, await fixture.GetTenantRefAsync(admin.Token));
    }

    /// <summary>An invited member, already in a workspace so their effective set is observable.</summary>
    private async Task<(ServicesFixture.TestUser Member, Guid MemberRef, Guid WorkspaceRef)> ArrangeMemberAsync(
        ServicesFixture.TestUser admin, Guid tenantRef)
    {
        var workspaceRef = await fixture.CreateWorkspaceAsync(admin.Token);
        var member = await fixture.CreateUserInTenantAsync(admin.Token, tenantRef);

        await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/workspaces/{workspaceRef}/members"),
            admin.Token,
            new { Email = member.Email });

        var memberRef = await fixture.FindTenantMemberRefAsync(admin.Token, tenantRef, member.Email);
        memberRef.Should().NotBeNull();

        return (member, memberRef!.Value, workspaceRef);
    }

    private async Task<Guid> CreateRoleWithPermissionAsync(string token, Guid tenantRef, string slug)
    {
        var roleRef = await fixture.CreateRoleAsync(token, tenantRef);

        await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/roles/{roleRef}/permissions"),
            token,
            new { Slug = slug, IsDeny = false });

        return roleRef;
    }

    // ── Listing ───────────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task MEM_01_ListMembers_IncludesTheAdministrator()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();

        var (itemCount, totalCount) = await fixture.GetPageAsync(
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/members?take=200"), admin.Token);

        itemCount.Should().Be(1, "a freshly registered tenant contains only its creator");
        totalCount.Should().Be(1);
    }

    [SkippableFact]
    public async Task MEM_02_SearchMatchesOnEmail()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();
        var (member, _, _) = await ArrangeMemberAsync(admin, tenantRef);

        var (itemCount, _) = await fixture.GetPageAsync(
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/members?search={Uri.EscapeDataString(member.Email)}"),
            admin.Token);

        itemCount.Should().Be(1);
    }

    [SkippableFact]
    public async Task MEM_03_SearchWithNoMatches_ReturnsEmpty()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();

        var (itemCount, totalCount) = await fixture.GetPageAsync(
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/members?search=no-such-person-{Guid.NewGuid():N}"),
            admin.Token);

        itemCount.Should().Be(0);
        totalCount.Should().Be(0);
    }

    [SkippableFact]
    public async Task MEM_04_TotalCountReflectsTheFilterNotTheTenant()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();
        var (member, _, _) = await ArrangeMemberAsync(admin, tenantRef);

        var (_, unfiltered) = await fixture.GetPageAsync(
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/members?take=200"), admin.Token);
        var (_, filtered) = await fixture.GetPageAsync(
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/members?search={Uri.EscapeDataString(member.Email)}"),
            admin.Token);

        unfiltered.Should().Be(2);
        filtered.Should().Be(1, "the count is computed over the same filter as the page");
    }

    [SkippableFact]
    public async Task MEM_05_ListWithoutUsersRead_Returns403()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();
        var (member, _, _) = await ArrangeMemberAsync(admin, tenantRef);

        var response = await fixture.SendAsync(
            HttpMethod.Get, ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/members"), member.Token);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Read-back ─────────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task MEM_06_UserRoles_ReportTheirScope()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();
        var (_, memberRef, workspaceRef) = await ArrangeMemberAsync(admin, tenantRef);
        var roleRef = await fixture.CreateRoleAsync(admin.Token, tenantRef);

        await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/members/{memberRef}/roles/{roleRef}"),
            admin.Token,
            new { WorkspaceRef = (Guid?)workspaceRef });

        var assignments = await fixture.GetUserRoleAssignmentsAsync(admin.Token, tenantRef, memberRef);

        assignments.Should().ContainSingle(a => a.RoleRef == roleRef && a.WorkspaceRef == workspaceRef);
    }

    [SkippableFact]
    public async Task MEM_07_UserPermissions_ReportDenyAndScope()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();
        var (_, memberRef, _) = await ArrangeMemberAsync(admin, tenantRef);

        await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/members/{memberRef}/permissions"),
            admin.Token,
            new { Slug = "logs.read", IsDeny = true, WorkspaceRef = (Guid?)null });

        var permissions = await fixture.GetUserPermissionsAsync(admin.Token, tenantRef, memberRef);

        // This endpoint is where an unexpected allow or deny gets diagnosed, so the deny flag and
        // the scope both have to survive the round trip.
        permissions.Should().ContainSingle(p => p.Slug == "logs.read" && p.IsDeny && p.WorkspaceRef == null);
    }

    [SkippableFact]
    public async Task MEM_08_UserGroups_ListTheGroupsTheyBelongTo()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();
        var (_, memberRef, _) = await ArrangeMemberAsync(admin, tenantRef);
        var groupRef = await fixture.CreateGroupAsync(admin.Token, tenantRef);

        var before = await fixture.GetUserGroupsAsync(admin.Token, tenantRef, memberRef);
        before.Should().BeEmpty("an invited member joins no groups");

        await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/members/{memberRef}/groups/{groupRef}"),
            admin.Token);

        (await fixture.GetUserGroupsAsync(admin.Token, tenantRef, memberRef))
            .Should().ContainSingle(g => g.GroupRef == groupRef);
    }

    [SkippableFact]
    public async Task MEM_09_ReadBackForAnUnknownUserRef_Returns403()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();

        var response = await fixture.SendAsync(
            HttpMethod.Get,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/members/{Guid.NewGuid()}/roles"),
            admin.Token);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ServicesFixture.ReadErrorCodeAsync(response)).Should().Be("USER_NOT_FOUND");
    }

    [SkippableFact]
    public async Task MEM_09b_AUserFromAnotherTenant_IsNotAddressable()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (adminA, tenantA) = await ArrangeAsync();
        var (_, foreignMemberRef, _) = await ArrangeMemberAsync(adminA, tenantA);

        var (adminB, tenantB) = await ArrangeAsync();

        // The invariant ManagementService.ResolveUserAsync exists to enforce: an administrator
        // cannot reach an outside account and pull it into their tenant's permission graph.
        var response = await fixture.SendAsync(
            HttpMethod.Get,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantB}/members/{foreignMemberRef}/roles"),
            adminB.Token);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ServicesFixture.ReadErrorCodeAsync(response)).Should().Be("USER_NOT_FOUND");
    }

    [SkippableFact]
    public async Task MEM_10_ReadBackWithoutRolesRead_Returns403()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();
        var (member, memberRef, _) = await ArrangeMemberAsync(admin, tenantRef);

        var response = await fixture.SendAsync(
            HttpMethod.Get,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/members/{memberRef}/roles"),
            member.Token);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Group membership ──────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task MEM_11_ReadingGroupsWithoutUsersRead_Returns403()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();
        var (member, memberRef, _) = await ArrangeMemberAsync(admin, tenantRef);

        // The groups read gates on users.read, not the roles.read that MEM_10 covers.
        var response = await fixture.SendAsync(
            HttpMethod.Get,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/members/{memberRef}/groups"),
            member.Token);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ServicesFixture.ReadErrorCodeAsync(response)).Should().Be("PERMISSION_UNAUTHORIZED");
    }

    [SkippableFact]
    public async Task MEM_12_AddUserToGroup_ShowsInTheirGroups()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();
        var (_, memberRef, _) = await ArrangeMemberAsync(admin, tenantRef);
        var groupRef = await fixture.CreateGroupAsync(admin.Token, tenantRef);

        var response = await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/members/{memberRef}/groups/{groupRef}"),
            admin.Token);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var body = await (await fixture.SendAsync(
            HttpMethod.Get,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/members/{memberRef}/groups"),
            admin.Token)).Content.ReadAsStringAsync();

        body.Should().Contain(groupRef.ToString());
    }

    [SkippableFact]
    public async Task MEM_13_GroupMembership_GrantsTheGroupsRoles()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();
        var (member, memberRef, workspaceRef) = await ArrangeMemberAsync(admin, tenantRef);
        var groupRef = await fixture.CreateGroupAsync(admin.Token, tenantRef);
        var roleRef = await CreateRoleWithPermissionAsync(admin.Token, tenantRef, "logs.read");

        await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/groups/{groupRef}/roles/{roleRef}"),
            admin.Token,
            new { WorkspaceRef = (Guid?)null });

        (await fixture.GetEffectivePermissionsAsync(member.Token, workspaceRef))
            .Should().NotContain("logs.read", "the role is on the group, and they are not in it yet");

        // Joining the group is what delivers the role — the assignment never names the user.
        await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/members/{memberRef}/groups/{groupRef}"),
            admin.Token);

        (await fixture.GetEffectivePermissionsAsync(member.Token, workspaceRef)).Should().Contain("logs.read");
    }

    [SkippableFact]
    public async Task MEM_14_AddingToTheSameGroupTwice_IsIdempotent()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();
        var (_, memberRef, _) = await ArrangeMemberAsync(admin, tenantRef);
        var groupRef = await fixture.CreateGroupAsync(admin.Token, tenantRef);
        var url = ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/members/{memberRef}/groups/{groupRef}");

        await fixture.SendAsync(HttpMethod.Post, url, admin.Token);
        var second = await fixture.SendAsync(HttpMethod.Post, url, admin.Token);

        second.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [SkippableFact]
    public async Task MEM_15_AddingToAnUnknownGroup_Returns403()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();
        var (_, memberRef, _) = await ArrangeMemberAsync(admin, tenantRef);

        var response = await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/members/{memberRef}/groups/{Guid.NewGuid()}"),
            admin.Token);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ServicesFixture.ReadErrorCodeAsync(response)).Should().Be("GROUP_NOT_FOUND");
    }

    [SkippableFact]
    public async Task MEM_16_RemovingFromAGroup_TakesThePermissionsAway()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();
        var (member, memberRef, workspaceRef) = await ArrangeMemberAsync(admin, tenantRef);

        var groupRef = await fixture.CreateGroupAsync(admin.Token, tenantRef);
        var roleRef = await CreateRoleWithPermissionAsync(admin.Token, tenantRef, "logs.read");

        await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/groups/{groupRef}/roles/{roleRef}"),
            admin.Token,
            new { WorkspaceRef = (Guid?)null });

        var membershipUrl = ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/members/{memberRef}/groups/{groupRef}");
        await fixture.SendAsync(HttpMethod.Post, membershipUrl, admin.Token);

        (await fixture.GetEffectivePermissionsAsync(member.Token, workspaceRef)).Should().Contain("logs.read");

        (await fixture.SendAsync(HttpMethod.Delete, membershipUrl, admin.Token)).StatusCode
            .Should().Be(HttpStatusCode.NoContent);

        (await fixture.GetEffectivePermissionsAsync(member.Token, workspaceRef)).Should().NotContain("logs.read");
    }

    // ── Role assignment ───────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task MEM_17_RemovingFromAGroupTheyAreNotIn_IsIdempotent()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();
        var (_, memberRef, _) = await ArrangeMemberAsync(admin, tenantRef);
        var groupRef = await fixture.CreateGroupAsync(admin.Token, tenantRef);

        var response = await fixture.SendAsync(
            HttpMethod.Delete,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/members/{memberRef}/groups/{groupRef}"),
            admin.Token);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, "delete is idempotent");
    }

    [SkippableFact]
    public async Task MEM_18_TenantWideAssignment_AppliesInEveryWorkspace()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();
        var (member, memberRef, workspaceOne) = await ArrangeMemberAsync(admin, tenantRef);

        var workspaceTwo = await fixture.CreateWorkspaceAsync(admin.Token);
        await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/workspaces/{workspaceTwo}/members"),
            admin.Token,
            new { Email = member.Email });

        var roleRef = await CreateRoleWithPermissionAsync(admin.Token, tenantRef, "logs.read");

        await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/members/{memberRef}/roles/{roleRef}"),
            admin.Token,
            new { WorkspaceRef = (Guid?)null });

        (await fixture.GetEffectivePermissionsAsync(member.Token, workspaceOne)).Should().Contain("logs.read");
        (await fixture.GetEffectivePermissionsAsync(member.Token, workspaceTwo)).Should().Contain("logs.read");
    }

    [SkippableFact]
    public async Task MEM_19_WorkspaceScopedAssignment_AppliesOnlyThere()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();
        var (member, memberRef, workspaceOne) = await ArrangeMemberAsync(admin, tenantRef);

        var workspaceTwo = await fixture.CreateWorkspaceAsync(admin.Token);
        await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/workspaces/{workspaceTwo}/members"),
            admin.Token,
            new { Email = member.Email });

        var roleRef = await CreateRoleWithPermissionAsync(admin.Token, tenantRef, "logs.read");

        await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/members/{memberRef}/roles/{roleRef}"),
            admin.Token,
            new { WorkspaceRef = (Guid?)workspaceOne });

        (await fixture.GetEffectivePermissionsAsync(member.Token, workspaceOne)).Should().Contain("logs.read");
        (await fixture.GetEffectivePermissionsAsync(member.Token, workspaceTwo)).Should().NotContain("logs.read");
    }

    [SkippableFact]
    public async Task MEM_20_AssignWithUnknownRoleRef_Returns403()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();
        var (_, memberRef, _) = await ArrangeMemberAsync(admin, tenantRef);

        var response = await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/members/{memberRef}/roles/{Guid.NewGuid()}"),
            admin.Token,
            new { WorkspaceRef = (Guid?)null });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ServicesFixture.ReadErrorCodeAsync(response)).Should().Be("ROLE_NOT_FOUND");
    }

    [SkippableFact]
    public async Task MEM_21_AssignScopedToAnotherTenantsWorkspace_IsRejected()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (adminA, tenantA) = await ArrangeAsync();
        var foreignWorkspace = await fixture.CreateWorkspaceAsync(adminA.Token);

        var (adminB, tenantB) = await ArrangeAsync();
        var (_, memberRef, _) = await ArrangeMemberAsync(adminB, tenantB);
        var roleRef = await fixture.CreateRoleAsync(adminB.Token, tenantB);

        // The reference resolves — it is a real workspace — but the procedure refuses to scope a
        // tenant-B assignment to a tenant-A workspace.
        var response = await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantB}/members/{memberRef}/roles/{roleRef}"),
            adminB.Token,
            new { WorkspaceRef = (Guid?)foreignWorkspace });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ServicesFixture.ReadErrorCodeAsync(response)).Should().Be("AUTH_OPERATION_REJECTED");
    }

    [SkippableFact]
    public async Task MEM_22_RemovingARoleAssignment_TakesThePermissionsAway()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();
        var (member, memberRef, workspaceRef) = await ArrangeMemberAsync(admin, tenantRef);
        var roleRef = await CreateRoleWithPermissionAsync(admin.Token, tenantRef, "logs.read");

        await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/members/{memberRef}/roles/{roleRef}"),
            admin.Token,
            new { WorkspaceRef = (Guid?)null });

        (await fixture.GetEffectivePermissionsAsync(member.Token, workspaceRef)).Should().Contain("logs.read");

        var response = await fixture.SendAsync(
            HttpMethod.Delete,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/members/{memberRef}/roles/{roleRef}"),
            admin.Token);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await fixture.GetEffectivePermissionsAsync(member.Token, workspaceRef)).Should().NotContain("logs.read");
        (await fixture.GetUserRoleAssignmentsAsync(admin.Token, tenantRef, memberRef))
            .Should().NotContain(a => a.RoleRef == roleRef);
    }

    [SkippableFact]
    public async Task MEM_23_ScopedAndTenantWideAssignmentsCoexist()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();
        var (member, memberRef, workspaceRef) = await ArrangeMemberAsync(admin, tenantRef);
        var roleRef = await CreateRoleWithPermissionAsync(admin.Token, tenantRef, "logs.read");
        var url = ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/members/{memberRef}/roles/{roleRef}");

        await fixture.SendAsync(HttpMethod.Post, url, admin.Token, new { WorkspaceRef = (Guid?)null });
        await fixture.SendAsync(HttpMethod.Post, url, admin.Token, new { WorkspaceRef = (Guid?)workspaceRef });

        await fixture.SendAsync(HttpMethod.Delete, $"{url}?workspaceRef={workspaceRef}", admin.Token);

        (await fixture.GetEffectivePermissionsAsync(member.Token, workspaceRef))
            .Should().Contain("logs.read", "removing the scoped assignment leaves the tenant-wide one");
    }

    // ── Direct user permissions ───────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task MEM_24_DirectGrant_ReachesTheEffectiveSet()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();
        var (member, memberRef, workspaceRef) = await ArrangeMemberAsync(admin, tenantRef);

        await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/members/{memberRef}/permissions"),
            admin.Token,
            new { Slug = "logs.read", IsDeny = false, WorkspaceRef = (Guid?)null });

        (await fixture.GetEffectivePermissionsAsync(member.Token, workspaceRef)).Should().Contain("logs.read");
    }

    [SkippableFact]
    public async Task MEM_25_DirectDeny_OverridesARoleGrant()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();
        var (member, memberRef, workspaceRef) = await ArrangeMemberAsync(admin, tenantRef);
        var roleRef = await CreateRoleWithPermissionAsync(admin.Token, tenantRef, "logs.read");

        await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/members/{memberRef}/roles/{roleRef}"),
            admin.Token,
            new { WorkspaceRef = (Guid?)null });

        (await fixture.GetEffectivePermissionsAsync(member.Token, workspaceRef)).Should().Contain("logs.read");

        await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/members/{memberRef}/permissions"),
            admin.Token,
            new { Slug = "logs.read", IsDeny = true, WorkspaceRef = (Guid?)null });

        // User-Deny outranks Role-Allow — the override is how an administrator carves an exception
        // out of a role without editing the role for everyone.
        (await fixture.GetEffectivePermissionsAsync(member.Token, workspaceRef)).Should().NotContain("logs.read");
    }

    [SkippableFact]
    public async Task MEM_26_DirectGrantScopedToAWorkspace_StaysThere()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();
        var (member, memberRef, workspaceOne) = await ArrangeMemberAsync(admin, tenantRef);

        var workspaceTwo = await fixture.CreateWorkspaceAsync(admin.Token);
        await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/workspaces/{workspaceTwo}/members"),
            admin.Token,
            new { Email = member.Email });

        await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/members/{memberRef}/permissions"),
            admin.Token,
            new { Slug = "logs.read", IsDeny = false, WorkspaceRef = (Guid?)workspaceOne });

        (await fixture.GetEffectivePermissionsAsync(member.Token, workspaceOne)).Should().Contain("logs.read");
        (await fixture.GetEffectivePermissionsAsync(member.Token, workspaceTwo)).Should().NotContain("logs.read");
    }

    [SkippableFact]
    public async Task MEM_27_GrantingAnUnknownSlug_Returns400AndWritesNothing()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();
        var (_, memberRef, _) = await ArrangeMemberAsync(admin, tenantRef);

        var response = await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/members/{memberRef}/permissions"),
            admin.Token,
            new { Slug = "logs.raed", IsDeny = false, WorkspaceRef = (Guid?)null });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ServicesFixture.ReadErrorCodeAsync(response)).Should().Be("AUTH_OPERATION_REJECTED");

        (await fixture.GetUserPermissionsAsync(admin.Token, tenantRef, memberRef))
            .Should().NotContain(p => p.Slug == "logs.raed", "the rejection must reach the database too");
    }

    [SkippableFact]
    public async Task MEM_28_RemovingADirectEntry_RevertsToTheRoles()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();
        var (member, memberRef, workspaceRef) = await ArrangeMemberAsync(admin, tenantRef);
        var roleRef = await CreateRoleWithPermissionAsync(admin.Token, tenantRef, "logs.read");

        await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/members/{memberRef}/roles/{roleRef}"),
            admin.Token,
            new { WorkspaceRef = (Guid?)null });

        await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/members/{memberRef}/permissions"),
            admin.Token,
            new { Slug = "logs.read", IsDeny = true, WorkspaceRef = (Guid?)null });

        (await fixture.GetEffectivePermissionsAsync(member.Token, workspaceRef)).Should().NotContain("logs.read");

        // Deleting the override is not the same as allowing it: the decision goes back to the roles.
        // Without a delete, an accidental deny could never be undone — an allow would just be a
        // second user-level row, and deny wins within that level.
        var response = await fixture.SendAsync(
            HttpMethod.Delete,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/members/{memberRef}/permissions/logs.read"),
            admin.Token);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await fixture.GetEffectivePermissionsAsync(member.Token, workspaceRef)).Should().Contain("logs.read");
    }

    [SkippableFact]
    public async Task MEM_29_RemovingADirectEntry_IsScopeSensitive()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();
        var (_, memberRef, workspaceRef) = await ArrangeMemberAsync(admin, tenantRef);
        var url = ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/members/{memberRef}/permissions");

        await fixture.SendAsync(HttpMethod.Post, url, admin.Token, new { Slug = "logs.read", IsDeny = false, WorkspaceRef = (Guid?)null });
        await fixture.SendAsync(HttpMethod.Post, url, admin.Token, new { Slug = "logs.read", IsDeny = false, WorkspaceRef = (Guid?)workspaceRef });

        (await fixture.GetUserPermissionsAsync(admin.Token, tenantRef, memberRef))
            .Where(p => p.Slug == "logs.read").Should().HaveCount(2, "scope is part of the row's identity");

        // Addressing the scoped row leaves the tenant-wide one standing.
        await fixture.SendAsync(
            HttpMethod.Delete,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/members/{memberRef}/permissions/logs.read?workspaceRef={workspaceRef}"),
            admin.Token);

        (await fixture.GetUserPermissionsAsync(admin.Token, tenantRef, memberRef))
            .Where(p => p.Slug == "logs.read").Should().ContainSingle()
            .Which.WorkspaceRef.Should().BeNull();
    }

    [SkippableFact]
    public async Task MEM_30_GrantWithoutRolesManage_Returns403()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();
        var (member, memberRef, _) = await ArrangeMemberAsync(admin, tenantRef);

        var response = await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/members/{memberRef}/permissions"),
            member.Token,
            new { Slug = "logs.read", IsDeny = false, WorkspaceRef = (Guid?)null });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Suspension ────────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task MEM_31_Suspension_ClosesThatTenantsWorkspaces()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();
        var (member, memberRef, workspaceRef) = await ArrangeMemberAsync(admin, tenantRef);

        (await fixture.ResolveWorkspaceAsync(member.Token, workspaceRef)).StatusCode
            .Should().Be(HttpStatusCode.OK, "the member can reach the workspace before the suspension");

        (await fixture.SetMemberSuspendedAsync(admin.Token, tenantRef, memberRef, isSuspended: true)).StatusCode
            .Should().Be(HttpStatusCode.NoContent);

        // Access is resolved per request, so the token the member already holds is refused here
        // straight away without being revoked.
        (await fixture.ResolveWorkspaceAsync(member.Token, workspaceRef)).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
        (await fixture.GetWorkspaceRefsAsync(member.Token)).Should().NotContain(workspaceRef);
        (await fixture.GetTenantsAsync(member.Token)).Select(t => t.RefId).Should().NotContain(tenantRef);
    }

    [SkippableFact]
    public async Task MEM_32_Suspension_LeavesTheAccountAndItsOtherTenantsAlone()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();
        var (member, memberRef, _) = await ArrangeMemberAsync(admin, tenantRef);

        // The invited member also administers the tenant registration gave them, so they are in two.
        var tenants = await fixture.GetTenantsAsync(member.Token);
        tenants.Should().HaveCountGreaterThan(1, "an invited member keeps the tenant registration gave them");
        var session = await fixture.LoginAsync(member.Email, member.Password);

        await fixture.SetMemberSuspendedAsync(admin.Token, tenantRef, memberRef, isSuspended: true);

        // An administrator of one tenant controls access to that tenant only: the person can still
        // sign in, refresh, and use everything they hold elsewhere.
        (await fixture.LoginRawAsync(member.Email, member.Password)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await fixture.RefreshAsync(session.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await fixture.GetTenantsAsync(member.Token)).Should().HaveCount(tenants.Count - 1);
    }

    [SkippableFact]
    public async Task MEM_33_LiftingTheSuspension_RestoresAccess()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();
        var (member, memberRef, workspaceRef) = await ArrangeMemberAsync(admin, tenantRef);

        await fixture.SetMemberSuspendedAsync(admin.Token, tenantRef, memberRef, isSuspended: true);
        (await fixture.SetMemberSuspendedAsync(admin.Token, tenantRef, memberRef, isSuspended: false)).StatusCode
            .Should().Be(HttpStatusCode.NoContent);

        (await fixture.ResolveWorkspaceAsync(member.Token, workspaceRef)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await fixture.GetTenantsAsync(member.Token)).Select(t => t.RefId).Should().Contain(tenantRef);
    }

    [SkippableFact]
    public async Task MEM_34_SuspensionIsVisibleInTheMemberList()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();
        var (member, memberRef, _) = await ArrangeMemberAsync(admin, tenantRef);

        await fixture.SetMemberSuspendedAsync(admin.Token, tenantRef, memberRef, isSuspended: true);

        var body = await (await fixture.SendAsync(
            HttpMethod.Get,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/members?search={Uri.EscapeDataString(member.Email)}"),
            admin.Token)).Content.ReadAsStringAsync();

        body.Should().Contain("\"isSuspended\":true").And.Contain("\"isActive\":true");
    }

    [SkippableFact]
    public async Task MEM_35_SuspendWithoutUsersManage_Returns403()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();
        var (member, memberRef, _) = await ArrangeMemberAsync(admin, tenantRef);

        var response = await fixture.SetMemberSuspendedAsync(member.Token, tenantRef, memberRef, isSuspended: true);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [SkippableFact]
    public async Task MEM_36_SuspendingAnUnknownUserRef_Returns403()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();

        var response = await fixture.SetMemberSuspendedAsync(admin.Token, tenantRef, Guid.NewGuid(), isSuspended: true);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ServicesFixture.ReadErrorCodeAsync(response)).Should().Be("USER_NOT_FOUND");
    }

    [SkippableFact]
    public async Task MEM_37_AnAdministratorCannotSuspendThemselves()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();
        var adminRef = await fixture.FindTenantMemberRefAsync(admin.Token, tenantRef, admin.Email);
        adminRef.Should().NotBeNull();

        // A suspended member holds nothing in the tenant, so nobody could lift a self-suspension.
        var response = await fixture.SetMemberSuspendedAsync(admin.Token, tenantRef, adminRef!.Value, isSuspended: true);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ServicesFixture.ReadErrorCodeAsync(response)).Should().Be("AUTH_CANNOT_SUSPEND_SELF");
        (await fixture.GetTenantsAsync(admin.Token)).Select(t => t.RefId).Should().Contain(tenantRef);
    }

    [SkippableFact]
    public async Task MEM_37b_TheAccountDeactivationRouteIsGone()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();
        var (_, memberRef, _) = await ArrangeMemberAsync(admin, tenantRef);

        // A tenant administrator used to be able to disable the whole account, locking the person
        // out of tenants that administrator had no say over. Suspension replaced it.
        var response = await fixture.SendAsync(
            HttpMethod.Put,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/members/{memberRef}/active"),
            admin.Token,
            new { IsActive = false });

        response.IsSuccessStatusCode.Should().BeFalse();
    }
}
