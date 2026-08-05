using System.Net;
using FluentAssertions;
using Wbskt.E2E.FeatureTests.Fixtures;

namespace Wbskt.E2E.FeatureTests.Scenarios.Auth;

/// <summary>
/// Sections 12.4 and 12.5 of Docs/Auth.Host.E2E.Scenarios.md — groups and the roles assigned to
/// them.
///
/// Groups exist to make permissions assignable to a set of people rather than one at a time, and
/// they nest: a member of a child group inherits roles assigned to its ancestors. GRL_05 is the
/// scenario that pins that inheritance, and GRL_06 pins that a workspace-scoped group assignment
/// stays in its workspace.
///
/// Requires the auth host running. Skips gracefully when it is down.
/// </summary>
[Collection(E2ECollection.Name)]
public sealed class TenantGroupTests(ServicesFixture fixture)
{
    private async Task<(ServicesFixture.TestUser Admin, Guid TenantRef)> ArrangeAsync()
    {
        var admin = await fixture.CreateUserAsync();
        return (admin, await fixture.GetTenantRefAsync(admin.Token));
    }

    private Task<HttpResponseMessage> CreateGroupAsync(string token, Guid tenantRef, string name, Guid? parent = null) =>
        fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/groups"),
            token,
            new { Name = name, ParentGroupRef = parent });

    /// <summary>Adds a member to a workspace so their effective set can be resolved there.</summary>
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

    /// <summary>A role carrying one slug, for observing what an assignment actually delivers.</summary>
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

    // ── Groups (§12.4) ────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task GRP_01_CreateTopLevelGroup_HasNoParent()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();
        var groupRef = await fixture.CreateGroupAsync(admin.Token, tenantRef);

        (await fixture.GetGroupsAsync(admin.Token, tenantRef))
            .Should().ContainSingle(g => g.RefId == groupRef && g.ParentGroupRefId == null);
    }

    [SkippableFact]
    public async Task GRP_02_CreateNestedGroup_EchoesTheParent()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();
        var parentRef = await fixture.CreateGroupAsync(admin.Token, tenantRef);

        var response = await CreateGroupAsync(admin.Token, tenantRef, $"e2e-child-{Guid.NewGuid():N}", parentRef);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        (await fixture.GetGroupsAsync(admin.Token, tenantRef))
            .Should().Contain(g => g.ParentGroupRefId == parentRef);
    }

    [SkippableFact]
    public async Task GRP_04_DuplicateNameInTheSameTenant_Returns409()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();
        var name = $"e2e-group-{Guid.NewGuid():N}";

        (await CreateGroupAsync(admin.Token, tenantRef, name)).StatusCode.Should().Be(HttpStatusCode.OK);

        var second = await CreateGroupAsync(admin.Token, tenantRef, name);

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ServicesFixture.ReadErrorCodeAsync(second)).Should().Be("AUTH_GROUP_CONFLICT");
    }

    [SkippableFact]
    public async Task GRP_05_UnknownParentGroupRef_Returns403()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();

        var response = await CreateGroupAsync(
            admin.Token, tenantRef, $"e2e-orphan-{Guid.NewGuid():N}", Guid.NewGuid());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ServicesFixture.ReadErrorCodeAsync(response)).Should().Be("GROUP_NOT_FOUND");
    }

    [SkippableFact]
    public async Task GRP_05b_ParentFromAnotherTenant_Returns403()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (adminA, tenantA) = await ArrangeAsync();
        var foreignGroup = await fixture.CreateGroupAsync(adminA.Token, tenantA);

        var (adminB, tenantB) = await ArrangeAsync();

        // Nesting under another tenant's group would splice two permission graphs together.
        var response = await CreateGroupAsync(
            adminB.Token, tenantB, $"e2e-{Guid.NewGuid():N}", foreignGroup);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [SkippableFact]
    public async Task GRP_06_EmptyName_IsRejectedByValidation()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();

        (await CreateGroupAsync(admin.Token, tenantRef, string.Empty)).StatusCode
            .Should().Be(HttpStatusCode.BadRequest);
    }

    [SkippableFact]
    public async Task GRP_07_RenameGroup_IsReflectedInTheList()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();
        var groupRef = await fixture.CreateGroupAsync(admin.Token, tenantRef);
        var renamed = $"e2e-renamed-{Guid.NewGuid():N}";

        var response = await fixture.SendAsync(
            HttpMethod.Put,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/groups/{groupRef}"),
            admin.Token,
            new { Name = renamed });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await fixture.GetGroupsAsync(admin.Token, tenantRef))
            .Should().ContainSingle(g => g.RefId == groupRef && g.Name == renamed);
    }

    [SkippableFact]
    public async Task GRP_08_RenamingOntoAnExistingName_Returns409()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();
        var taken = $"e2e-taken-{Guid.NewGuid():N}";
        await fixture.CreateGroupAsync(admin.Token, tenantRef, taken);
        var groupRef = await fixture.CreateGroupAsync(admin.Token, tenantRef);

        var response = await fixture.SendAsync(
            HttpMethod.Put,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/groups/{groupRef}"),
            admin.Token,
            new { Name = taken });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [SkippableFact]
    public async Task GRP_09_DeleteLeafGroup_Succeeds()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();
        var groupRef = await fixture.CreateGroupAsync(admin.Token, tenantRef);

        var response = await fixture.SendAsync(
            HttpMethod.Delete,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/groups/{groupRef}"),
            admin.Token);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await fixture.GetGroupsAsync(admin.Token, tenantRef))
            .Select(g => g.RefId).Should().NotContain(groupRef);
    }

    [SkippableFact]
    public async Task GRP_10_DeletingAGroupWithChildren_IsRejected()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();
        var parentRef = await fixture.CreateGroupAsync(admin.Token, tenantRef);
        await CreateGroupAsync(admin.Token, tenantRef, $"e2e-child-{Guid.NewGuid():N}", parentRef);

        var response = await fixture.SendAsync(
            HttpMethod.Delete,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/groups/{parentRef}"),
            admin.Token);

        // Deleting a parent would orphan its children's inheritance silently, so the procedure
        // refuses and the guard reads back as a validation error rather than a server fault.
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ServicesFixture.ReadErrorCodeAsync(response)).Should().Be("AUTH_OPERATION_REJECTED");
    }

    [SkippableFact]
    public async Task GRP_11_DeletingChildThenParent_Succeeds()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();
        var parentRef = await fixture.CreateGroupAsync(admin.Token, tenantRef);

        var childResponse = await CreateGroupAsync(
            admin.Token, tenantRef, $"e2e-child-{Guid.NewGuid():N}", parentRef);
        childResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var childRef = (await fixture.GetGroupsAsync(admin.Token, tenantRef))
            .Single(g => g.ParentGroupRefId == parentRef).RefId;

        (await fixture.SendAsync(
            HttpMethod.Delete,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/groups/{childRef}"),
            admin.Token)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await fixture.SendAsync(
            HttpMethod.Delete,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/groups/{parentRef}"),
            admin.Token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [SkippableFact]
    public async Task GRP_12_DeletingAnUnknownGroup_Returns403()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();

        var response = await fixture.SendAsync(
            HttpMethod.Delete,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/groups/{Guid.NewGuid()}"),
            admin.Token);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ServicesFixture.ReadErrorCodeAsync(response)).Should().Be("GROUP_NOT_FOUND");
    }

    [SkippableFact]
    public async Task GRP_13_CreateWithoutUsersManage_Returns403()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();
        var member = await fixture.CreateUserInTenantAsync(admin.Token, tenantRef);

        var response = await CreateGroupAsync(member.Token, tenantRef, "unauthorized");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ServicesFixture.ReadErrorCodeAsync(response)).Should().Be("PERMISSION_UNAUTHORIZED");
    }

    [SkippableFact]
    public async Task GRP_14_ListWithoutUsersRead_Returns403()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();
        var member = await fixture.CreateUserInTenantAsync(admin.Token, tenantRef);

        var response = await fixture.SendAsync(
            HttpMethod.Get, ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/groups"), member.Token);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Group role assignments (§12.5) ────────────────────────────────────────────────────

    [SkippableFact]
    public async Task GRL_01_AssignRoleTenantWide_Succeeds()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();
        var groupRef = await fixture.CreateGroupAsync(admin.Token, tenantRef);
        var roleRef = await fixture.CreateRoleAsync(admin.Token, tenantRef);

        var response = await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/groups/{groupRef}/roles/{roleRef}"),
            admin.Token,
            new { WorkspaceRef = (Guid?)null });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [SkippableFact]
    public async Task GRL_03_AssignmentAppearsInTheGroupsRoleList()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();
        var workspaceRef = await fixture.CreateWorkspaceAsync(admin.Token);
        var groupRef = await fixture.CreateGroupAsync(admin.Token, tenantRef);
        var roleRef = await fixture.CreateRoleAsync(admin.Token, tenantRef);

        await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/groups/{groupRef}/roles/{roleRef}"),
            admin.Token,
            new { WorkspaceRef = (Guid?)workspaceRef });

        var assignments = await fixture.GetGroupRoleAssignmentsAsync(admin.Token, tenantRef, groupRef);

        assignments.Should().ContainSingle(a => a.RoleRef == roleRef && a.WorkspaceRef == workspaceRef);
    }

    [SkippableFact]
    public async Task GRL_04_GroupMemberInheritsTheRolesPermissions()
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
            .Should().NotContain("logs.read", "not in the group yet");

        await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/members/{memberRef}/groups/{groupRef}"),
            admin.Token);

        (await fixture.GetEffectivePermissionsAsync(member.Token, workspaceRef))
            .Should().Contain("logs.read", "group membership delivers the group's roles");
    }

    [SkippableFact]
    public async Task GRL_05_InheritanceFollowsTheParentChain()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();
        var (member, memberRef, workspaceRef) = await ArrangeMemberAsync(admin, tenantRef);

        var parentRef = await fixture.CreateGroupAsync(admin.Token, tenantRef);
        await CreateGroupAsync(admin.Token, tenantRef, $"e2e-child-{Guid.NewGuid():N}", parentRef);
        var childRef = (await fixture.GetGroupsAsync(admin.Token, tenantRef))
            .Single(g => g.ParentGroupRefId == parentRef).RefId;

        var roleRef = await CreateRoleWithPermissionAsync(admin.Token, tenantRef, "clients.read");

        // Role on the *parent*, membership in the *child*.
        await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/groups/{parentRef}/roles/{roleRef}"),
            admin.Token,
            new { WorkspaceRef = (Guid?)null });

        await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/members/{memberRef}/groups/{childRef}"),
            admin.Token);

        (await fixture.GetEffectivePermissionsAsync(member.Token, workspaceRef))
            .Should().Contain("clients.read", "a child group inherits its ancestors' roles");
    }

    [SkippableFact]
    public async Task GRL_06_WorkspaceScopedGroupRole_StaysInItsWorkspace()
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

        var groupRef = await fixture.CreateGroupAsync(admin.Token, tenantRef);
        var roleRef = await CreateRoleWithPermissionAsync(admin.Token, tenantRef, "logs.read");

        await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/groups/{groupRef}/roles/{roleRef}"),
            admin.Token,
            new { WorkspaceRef = (Guid?)workspaceOne });

        await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/members/{memberRef}/groups/{groupRef}"),
            admin.Token);

        (await fixture.GetEffectivePermissionsAsync(member.Token, workspaceOne)).Should().Contain("logs.read");
        (await fixture.GetEffectivePermissionsAsync(member.Token, workspaceTwo)).Should().NotContain("logs.read");
    }

    [SkippableFact]
    public async Task GRL_07_AssignWithUnknownRoleRef_Returns403()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();
        var groupRef = await fixture.CreateGroupAsync(admin.Token, tenantRef);

        var response = await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/groups/{groupRef}/roles/{Guid.NewGuid()}"),
            admin.Token,
            new { WorkspaceRef = (Guid?)null });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ServicesFixture.ReadErrorCodeAsync(response)).Should().Be("ROLE_NOT_FOUND");
    }

    [SkippableFact]
    public async Task GRL_08_AssignWithUnknownGroupRef_Returns403()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();
        var roleRef = await fixture.CreateRoleAsync(admin.Token, tenantRef);

        var response = await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/groups/{Guid.NewGuid()}/roles/{roleRef}"),
            admin.Token,
            new { WorkspaceRef = (Guid?)null });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ServicesFixture.ReadErrorCodeAsync(response)).Should().Be("GROUP_NOT_FOUND");
    }

    [SkippableFact]
    public async Task GRL_09_AssignWithUnknownWorkspaceRef_Returns403()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();
        var groupRef = await fixture.CreateGroupAsync(admin.Token, tenantRef);
        var roleRef = await fixture.CreateRoleAsync(admin.Token, tenantRef);

        var response = await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/groups/{groupRef}/roles/{roleRef}"),
            admin.Token,
            new { WorkspaceRef = (Guid?)Guid.NewGuid() });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ServicesFixture.ReadErrorCodeAsync(response)).Should().Be("WORKSPACE_NOT_FOUND");
    }

    [SkippableFact]
    public async Task GRL_10_AssignWithNoBody_IsRejected()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();
        var groupRef = await fixture.CreateGroupAsync(admin.Token, tenantRef);
        var roleRef = await fixture.CreateRoleAsync(admin.Token, tenantRef);

        // The scope body is mandatory even for a tenant-wide assignment, so "assign this role
        // everywhere" cannot be expressed as a bodyless POST. Pinning the current shape.
        var response = await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/groups/{groupRef}/roles/{roleRef}"),
            admin.Token);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [SkippableFact]
    public async Task GRL_11_AssigningTwice_IsIdempotent()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();
        var groupRef = await fixture.CreateGroupAsync(admin.Token, tenantRef);
        var roleRef = await fixture.CreateRoleAsync(admin.Token, tenantRef);
        var url = ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/groups/{groupRef}/roles/{roleRef}");

        await fixture.SendAsync(HttpMethod.Post, url, admin.Token, new { WorkspaceRef = (Guid?)null });
        var second = await fixture.SendAsync(HttpMethod.Post, url, admin.Token, new { WorkspaceRef = (Guid?)null });

        second.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await fixture.GetGroupRoleAssignmentsAsync(admin.Token, tenantRef, groupRef))
            .Count(a => a.RoleRef == roleRef).Should().Be(1);
    }

    [SkippableFact]
    public async Task GRL_12_RemoveAssignment_TakesThePermissionsAway()
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

        await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/members/{memberRef}/groups/{groupRef}"),
            admin.Token);

        (await fixture.GetEffectivePermissionsAsync(member.Token, workspaceRef)).Should().Contain("logs.read");

        // Scope travels on the query string here, not in a body — DELETE with a body is awkward.
        var response = await fixture.SendAsync(
            HttpMethod.Delete,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/groups/{groupRef}/roles/{roleRef}"),
            admin.Token);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await fixture.GetEffectivePermissionsAsync(member.Token, workspaceRef)).Should().NotContain("logs.read");
    }

    [SkippableFact]
    public async Task GRL_13_RemovingAScopedAssignment_LeavesTheTenantWideOne()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();
        var (member, memberRef, workspaceRef) = await ArrangeMemberAsync(admin, tenantRef);

        var groupRef = await fixture.CreateGroupAsync(admin.Token, tenantRef);
        var roleRef = await CreateRoleWithPermissionAsync(admin.Token, tenantRef, "logs.read");
        var url = ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/groups/{groupRef}/roles/{roleRef}");

        await fixture.SendAsync(HttpMethod.Post, url, admin.Token, new { WorkspaceRef = (Guid?)null });
        await fixture.SendAsync(HttpMethod.Post, url, admin.Token, new { WorkspaceRef = (Guid?)workspaceRef });

        await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/members/{memberRef}/groups/{groupRef}"),
            admin.Token);

        // Scope is part of an assignment's identity, so removing one must not touch the other.
        await fixture.SendAsync(HttpMethod.Delete, $"{url}?workspaceRef={workspaceRef}", admin.Token);

        (await fixture.GetEffectivePermissionsAsync(member.Token, workspaceRef))
            .Should().Contain("logs.read", "the tenant-wide assignment still applies");
    }

    [SkippableFact]
    public async Task GRL_14_RemovingANonExistentAssignment_IsIdempotent()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();
        var groupRef = await fixture.CreateGroupAsync(admin.Token, tenantRef);
        var roleRef = await fixture.CreateRoleAsync(admin.Token, tenantRef);

        var response = await fixture.SendAsync(
            HttpMethod.Delete,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/groups/{groupRef}/roles/{roleRef}"),
            admin.Token);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [SkippableFact]
    public async Task GRL_15_AssignWithoutRolesManage_Returns403()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();
        var groupRef = await fixture.CreateGroupAsync(admin.Token, tenantRef);
        var roleRef = await fixture.CreateRoleAsync(admin.Token, tenantRef);
        var member = await fixture.CreateUserInTenantAsync(admin.Token, tenantRef);

        var response = await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/groups/{groupRef}/roles/{roleRef}"),
            member.Token,
            new { WorkspaceRef = (Guid?)null });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
