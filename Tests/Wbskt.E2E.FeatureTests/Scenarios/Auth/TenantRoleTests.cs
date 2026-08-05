using System.Net;
using FluentAssertions;
using Wbskt.E2E.FeatureTests.Fixtures;

namespace Wbskt.E2E.FeatureTests.Scenarios.Auth;

/// <summary>
/// Sections 12.2 and 12.3 of Docs/Auth.Host.E2E.Scenarios.md — tenant roles and the permissions
/// attached to them.
///
/// Every scenario runs as an administrator of a tenant they created by registering, since
/// registration provisions a tenant with a tenant-wide Admin assignment. The negative cases use an
/// *invited* member instead: they belong to the tenant holding nothing, which is the only way to
/// arrange an unprivileged caller now that registering alone makes you an administrator somewhere.
///
/// Requires the auth host running. Skips gracefully when it is down.
/// </summary>
[Collection(E2ECollection.Name)]
public sealed class TenantRoleTests(ServicesFixture fixture)
{
    private async Task<(ServicesFixture.TestUser Admin, Guid TenantRef)> ArrangeAsync()
    {
        var admin = await fixture.CreateUserAsync();
        return (admin, await fixture.GetTenantRefAsync(admin.Token));
    }

    private Task<HttpResponseMessage> CreateRoleAsync(string token, Guid tenantRef, string name, string? description = null) =>
        fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/roles"),
            token,
            new { Name = name, Description = description });

    // ── Creating ──────────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task ROLE_01_CreateRole_ReturnsTheCreatedRole()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();
        var name = $"e2e-role-{Guid.NewGuid():N}";

        var response = await CreateRoleAsync(admin.Token, tenantRef, name, "a description");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [SkippableFact]
    public async Task ROLE_02_CreatedRole_AppearsInTheList()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();
        var roleRef = await fixture.CreateRoleAsync(admin.Token, tenantRef);

        (await fixture.GetRolesAsync(admin.Token, tenantRef))
            .Select(r => r.RefId).Should().Contain(roleRef);
    }

    [SkippableFact]
    public async Task ROLE_02b_NewTenant_StartsWithAdminAndUser()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();

        // Registration provisions these two alongside the tenant; without Admin the creator would
        // have no way to administer what they just created.
        (await fixture.GetRolesAsync(admin.Token, tenantRef))
            .Select(r => r.Name).Should().Contain(["Admin", "User"]);
    }

    [SkippableFact]
    public async Task ROLE_03_CreateWithNullDescription_Succeeds()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();

        var response = await CreateRoleAsync(admin.Token, tenantRef, $"e2e-{Guid.NewGuid():N}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [SkippableFact]
    public async Task ROLE_04_DuplicateNameInTheSameTenant_Returns409()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();
        var name = $"e2e-dupe-{Guid.NewGuid():N}";

        (await CreateRoleAsync(admin.Token, tenantRef, name)).StatusCode.Should().Be(HttpStatusCode.OK);

        var second = await CreateRoleAsync(admin.Token, tenantRef, name);

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ServicesFixture.ReadErrorCodeAsync(second)).Should().Be("AUTH_ROLE_CONFLICT");
    }

    [SkippableFact]
    public async Task ROLE_04b_SameNameInADifferentTenant_IsAllowed()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (adminA, tenantA) = await ArrangeAsync();
        var (adminB, tenantB) = await ArrangeAsync();
        var name = $"e2e-shared-{Guid.NewGuid():N}";

        (await CreateRoleAsync(adminA.Token, tenantA, name)).StatusCode.Should().Be(HttpStatusCode.OK);

        // Roles are tenant-owned, so uniqueness is per tenant and not global.
        (await CreateRoleAsync(adminB.Token, tenantB, name)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [SkippableFact]
    public async Task ROLE_05_EmptyName_IsRejectedByValidation()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();

        (await CreateRoleAsync(admin.Token, tenantRef, string.Empty)).StatusCode
            .Should().Be(HttpStatusCode.BadRequest);
    }

    [SkippableFact]
    public async Task ROLE_06_OverlongName_IsRejectedByValidation()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();

        (await CreateRoleAsync(admin.Token, tenantRef, new string('x', 101))).StatusCode
            .Should().Be(HttpStatusCode.BadRequest);
    }

    [SkippableFact]
    public async Task ROLE_07_OverlongDescription_IsRejectedByValidation()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();

        (await CreateRoleAsync(admin.Token, tenantRef, "valid", new string('x', 256))).StatusCode
            .Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Updating and deleting ─────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task ROLE_08_RenameRole_IsReflectedInTheList()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();
        var roleRef = await fixture.CreateRoleAsync(admin.Token, tenantRef);
        var renamed = $"e2e-renamed-{Guid.NewGuid():N}";

        var response = await fixture.SendAsync(
            HttpMethod.Put,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/roles/{roleRef}"),
            admin.Token,
            new { Name = renamed, Description = "updated" });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await fixture.GetRolesAsync(admin.Token, tenantRef))
            .Should().ContainSingle(r => r.RefId == roleRef && r.Name == renamed);
    }

    [SkippableFact]
    public async Task ROLE_09_RenamingOntoAnExistingName_Returns409()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();
        var taken = $"e2e-taken-{Guid.NewGuid():N}";
        await fixture.CreateRoleAsync(admin.Token, tenantRef, taken);
        var roleRef = await fixture.CreateRoleAsync(admin.Token, tenantRef);

        var response = await fixture.SendAsync(
            HttpMethod.Put,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/roles/{roleRef}"),
            admin.Token,
            new { Name = taken, Description = (string?)null });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [SkippableFact]
    public async Task ROLE_10_UpdatingAnUnknownRole_Returns403()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();

        var response = await fixture.SendAsync(
            HttpMethod.Put,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/roles/{Guid.NewGuid()}"),
            admin.Token,
            new { Name = "whatever", Description = (string?)null });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ServicesFixture.ReadErrorCodeAsync(response)).Should().Be("ROLE_NOT_FOUND");
    }

    [SkippableFact]
    public async Task ROLE_11_DeleteRole_RemovesItFromTheList()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();
        var roleRef = await fixture.CreateRoleAsync(admin.Token, tenantRef);

        var response = await fixture.SendAsync(
            HttpMethod.Delete,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/roles/{roleRef}"),
            admin.Token);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await fixture.GetRolesAsync(admin.Token, tenantRef))
            .Select(r => r.RefId).Should().NotContain(roleRef);
    }

    [SkippableFact]
    public async Task ROLE_12_DeletingARole_StripsItFromWhoeverHeldIt()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();
        var workspaceRef = await fixture.CreateWorkspaceAsync(admin.Token);

        var member = await fixture.CreateUserInTenantAsync(admin.Token, tenantRef);
        await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/workspaces/{workspaceRef}/members"),
            admin.Token,
            new { Email = member.Email });

        var memberRef = await fixture.FindTenantMemberRefAsync(admin.Token, tenantRef, member.Email);
        var roleRef = await fixture.CreateRoleAsync(admin.Token, tenantRef);

        await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/roles/{roleRef}/permissions"),
            admin.Token,
            new { Slug = "logs.read", IsDeny = false });

        await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/members/{memberRef}/roles/{roleRef}"),
            admin.Token,
            new { WorkspaceRef = (Guid?)null });

        (await fixture.GetEffectivePermissionsAsync(member.Token, workspaceRef))
            .Should().Contain("logs.read");

        await fixture.SendAsync(
            HttpMethod.Delete,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/roles/{roleRef}"),
            admin.Token);

        // Deleting a role takes its permissions with it, so anyone who held it loses them.
        (await fixture.GetEffectivePermissionsAsync(member.Token, workspaceRef))
            .Should().NotContain("logs.read");
    }

    [SkippableFact]
    public async Task ROLE_13_DeletingAnUnknownRole_Returns403()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();

        var response = await fixture.SendAsync(
            HttpMethod.Delete,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/roles/{Guid.NewGuid()}"),
            admin.Token);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Authorization ─────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task ROLE_14_CreateWithoutRolesManage_Returns403()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();
        var member = await fixture.CreateUserInTenantAsync(admin.Token, tenantRef);

        var response = await CreateRoleAsync(member.Token, tenantRef, "unauthorized");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ServicesFixture.ReadErrorCodeAsync(response)).Should().Be("PERMISSION_UNAUTHORIZED");
    }

    [SkippableFact]
    public async Task ROLE_15_ListWithoutRolesRead_Returns403()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();
        var member = await fixture.CreateUserInTenantAsync(admin.Token, tenantRef);

        var response = await fixture.SendAsync(
            HttpMethod.Get, ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/roles"), member.Token);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [SkippableFact]
    public async Task ROLE_16_UnknownTenantRef_Returns403()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var admin = await fixture.CreateUserAsync();

        var response = await fixture.SendAsync(
            HttpMethod.Get, ServicesFixture.AuthUrl($"/api/tenants/{Guid.NewGuid()}/roles"), admin.Token);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ServicesFixture.ReadErrorCodeAsync(response)).Should().Be("TENANT_NOT_FOUND");
    }

    [SkippableFact]
    public async Task ROLE_16b_AnotherTenantsRoles_AreNotReadable()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (_, tenantA) = await ArrangeAsync();
        var outsider = await fixture.CreateUserAsync();

        // A real tenant, but not one the caller belongs to — reported the same as a nonexistent one.
        var response = await fixture.SendAsync(
            HttpMethod.Get, ServicesFixture.AuthUrl($"/api/tenants/{tenantA}/roles"), outsider.Token);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ServicesFixture.ReadErrorCodeAsync(response)).Should().Be("TENANT_NOT_FOUND");
    }

    [SkippableFact]
    public async Task ROLE_16c_ARoleRefFromAnotherTenant_DoesNotResolve()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (adminA, tenantA) = await ArrangeAsync();
        var foreignRole = await fixture.CreateRoleAsync(adminA.Token, tenantA);

        var (adminB, tenantB) = await ArrangeAsync();

        // Reference resolution is tenant-scoped, so B cannot reach into A's graph by guessing a ref.
        var response = await fixture.SendAsync(
            HttpMethod.Delete,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantB}/roles/{foreignRole}"),
            adminB.Token);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ServicesFixture.ReadErrorCodeAsync(response)).Should().Be("ROLE_NOT_FOUND");
    }

    [SkippableFact]
    public async Task ROLE_17_RolesManageHeldWorkspaceScopedOnly_DoesNotReachTenantAdministration()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();
        var workspaceRef = await fixture.CreateWorkspaceAsync(admin.Token);
        var member = await fixture.CreateUserInTenantAsync(admin.Token, tenantRef);

        await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/workspaces/{workspaceRef}/members"),
            admin.Token,
            new { Email = member.Email });

        var memberRef = await fixture.FindTenantMemberRefAsync(admin.Token, tenantRef, member.Email);
        var roleRef = await fixture.CreateRoleAsync(admin.Token, tenantRef);

        foreach (var slug in new[] { "roles.manage", "roles.read" })
        {
            await fixture.SendAsync(
                HttpMethod.Post,
                ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/roles/{roleRef}/permissions"),
                admin.Token,
                new { Slug = slug, IsDeny = false });
        }

        // Assigned to one workspace rather than tenant-wide: the capability is real, but pinned.
        await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/members/{memberRef}/roles/{roleRef}"),
            admin.Token,
            new { WorkspaceRef = (Guid?)workspaceRef });

        (await fixture.GetEffectivePermissionsAsync(member.Token, workspaceRef))
            .Should().Contain("roles.manage", "the grant is genuinely in effect inside its workspace");

        var response = await CreateRoleAsync(member.Token, tenantRef, $"e2e-{Guid.NewGuid():N}");

        response.StatusCode.Should().Be(
            HttpStatusCode.Forbidden,
            "tenant administration verifies the slug with a null workspace, which a scoped grant does not satisfy");
        (await ServicesFixture.ReadErrorCodeAsync(response)).Should().Be("PERMISSION_UNAUTHORIZED");
    }

    [SkippableFact]
    public async Task ROLE_18_NonGuidTenantRef_IsRejectedByRouting()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var admin = await fixture.CreateUserAsync();

        // The :guid route constraint rejects it before any action runs, so this is a routing 404
        // rather than the 403 an unresolvable-but-well-formed reference gets.
        var response = await fixture.SendAsync(
            HttpMethod.Get, ServicesFixture.AuthUrl("/api/tenants/not-a-guid/roles"), admin.Token);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Role permissions (§12.3) ──────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task RPERM_01_GrantSlug_AppearsOnTheRole()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();
        var roleRef = await fixture.CreateRoleAsync(admin.Token, tenantRef);

        var response = await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/roles/{roleRef}/permissions"),
            admin.Token,
            new { Slug = "logs.read", IsDeny = false });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await fixture.GetRolePermissionSlugsAsync(admin.Token, tenantRef, roleRef))
            .Should().Contain("logs.read");
    }

    [SkippableFact]
    public async Task RPERM_02_GrantAsDeny_IsListedAsADenial()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();
        var roleRef = await fixture.CreateRoleAsync(admin.Token, tenantRef);

        var response = await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/roles/{roleRef}/permissions"),
            admin.Token,
            new { Slug = "logs.read", IsDeny = true });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Attached either way, so the deny flag is the only thing separating this from a grant.
        (await fixture.GetRolePermissionsAsync(admin.Token, tenantRef, roleRef))
            .Should().ContainSingle(p => p.Slug == "logs.read").Which.IsDeny.Should().BeTrue();
    }

    [SkippableFact]
    public async Task RPERM_03_RegrantingFlipsTheDenyFlagInPlace()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();
        var roleRef = await fixture.CreateRoleAsync(admin.Token, tenantRef);
        var url = ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/roles/{roleRef}/permissions");

        await fixture.SendAsync(HttpMethod.Post, url, admin.Token, new { Slug = "logs.read", IsDeny = false });
        await fixture.SendAsync(HttpMethod.Post, url, admin.Token, new { Slug = "logs.read", IsDeny = true });

        // A MERGE, not an insert — the slug must appear once, now as a denial.
        (await fixture.GetRolePermissionSlugsAsync(admin.Token, tenantRef, roleRef))
            .Count(s => s == "logs.read").Should().Be(1);
    }

    [SkippableFact]
    public async Task RPERM_04_UnknownSlug_Returns400AndWritesNothing()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();
        var roleRef = await fixture.CreateRoleAsync(admin.Token, tenantRef);

        var response = await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/roles/{roleRef}/permissions"),
            admin.Token,
            new { Slug = "roles.mange", IsDeny = false });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ServicesFixture.ReadErrorCodeAsync(response)).Should().Be("AUTH_OPERATION_REJECTED");

        // The section-2 regression in its own right: the rejection must reach the database too.
        (await fixture.GetRolePermissionSlugsAsync(admin.Token, tenantRef, roleRef))
            .Should().NotContain("roles.mange");
    }

    [SkippableFact]
    public async Task RPERM_05_BodyCarryingWorkspaceRef_IsRejected()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();
        var roleRef = await fixture.CreateRoleAsync(admin.Token, tenantRef);

        // Role permissions are unscoped, so a workspace here could only ever be ignored. Unmapped
        // members are refused rather than dropped, turning a silent success into a named mistake.
        var response = await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/roles/{roleRef}/permissions"),
            admin.Token,
            new { Slug = "logs.read", IsDeny = false, WorkspaceRef = Guid.NewGuid() });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [SkippableFact]
    public async Task RPERM_06_EmptySlug_IsRejectedByValidation()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();
        var roleRef = await fixture.CreateRoleAsync(admin.Token, tenantRef);

        var response = await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/roles/{roleRef}/permissions"),
            admin.Token,
            new { Slug = string.Empty, IsDeny = false });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [SkippableFact]
    public async Task RPERM_07_OverlongSlug_IsRejectedByValidation()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();
        var roleRef = await fixture.CreateRoleAsync(admin.Token, tenantRef);

        var response = await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/roles/{roleRef}/permissions"),
            admin.Token,
            new { Slug = new string('s', 101), IsDeny = false });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [SkippableFact]
    public async Task RPERM_08_RemovePermission_TakesItOffTheRole()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();
        var roleRef = await fixture.CreateRoleAsync(admin.Token, tenantRef);

        await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/roles/{roleRef}/permissions"),
            admin.Token,
            new { Slug = "logs.read", IsDeny = false });

        var response = await fixture.SendAsync(
            HttpMethod.Delete,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/roles/{roleRef}/permissions/logs.read"),
            admin.Token);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await fixture.GetRolePermissionSlugsAsync(admin.Token, tenantRef, roleRef))
            .Should().NotContain("logs.read");
    }

    [SkippableFact]
    public async Task RPERM_09_RemovingAPermissionNeverGranted_IsIdempotent()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();
        var roleRef = await fixture.CreateRoleAsync(admin.Token, tenantRef);

        var response = await fixture.SendAsync(
            HttpMethod.Delete,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/roles/{roleRef}/permissions/logs.read"),
            admin.Token);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, "delete is idempotent");
    }

    [SkippableFact]
    public async Task RPERM_10_RemovingIsNotDenying()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();
        var workspaceRef = await fixture.CreateWorkspaceAsync(admin.Token);
        var member = await fixture.CreateUserInTenantAsync(admin.Token, tenantRef);

        await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/workspaces/{workspaceRef}/members"),
            admin.Token,
            new { Email = member.Email });

        var memberRef = await fixture.FindTenantMemberRefAsync(admin.Token, tenantRef, member.Email);

        // Two roles, both held tenant-wide: one is a standing source of the slug, the other is edited.
        var granting = await fixture.CreateRoleAsync(admin.Token, tenantRef);
        var editable = await fixture.CreateRoleAsync(admin.Token, tenantRef);

        await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/roles/{granting}/permissions"),
            admin.Token,
            new { Slug = "logs.read", IsDeny = false });

        foreach (var roleRef in new[] { granting, editable })
        {
            await fixture.SendAsync(
                HttpMethod.Post,
                ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/members/{memberRef}/roles/{roleRef}"),
                admin.Token,
                new { WorkspaceRef = (Guid?)null });
        }

        // Denying on the second role overrides the first — role-deny outranks role-allow.
        await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/roles/{editable}/permissions"),
            admin.Token,
            new { Slug = "logs.read", IsDeny = true });

        (await fixture.GetEffectivePermissionsAsync(member.Token, workspaceRef))
            .Should().NotContain("logs.read", "a denial anywhere in the role set wins");

        // Removing that denial is a different act from granting: the other role's allow returns.
        await fixture.SendAsync(
            HttpMethod.Delete,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/roles/{editable}/permissions/logs.read"),
            admin.Token);

        (await fixture.GetEffectivePermissionsAsync(member.Token, workspaceRef))
            .Should().Contain("logs.read", "removal returns the decision to the remaining sources");
    }

    [SkippableFact]
    public async Task RPERM_11_GrantWithoutRolesManage_Returns403()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();
        var roleRef = await fixture.CreateRoleAsync(admin.Token, tenantRef);
        var member = await fixture.CreateUserInTenantAsync(admin.Token, tenantRef);

        var response = await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/roles/{roleRef}/permissions"),
            member.Token,
            new { Slug = "logs.read", IsDeny = false });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [SkippableFact]
    public async Task RPERM_12_GrantedRolePermission_ReachesTheEffectiveSet()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();
        var workspaceRef = await fixture.CreateWorkspaceAsync(admin.Token);

        var member = await fixture.CreateUserInTenantAsync(admin.Token, tenantRef);
        await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/workspaces/{workspaceRef}/members"),
            admin.Token,
            new { Email = member.Email });

        var memberRef = await fixture.FindTenantMemberRefAsync(admin.Token, tenantRef, member.Email);
        var roleRef = await fixture.CreateRoleAsync(admin.Token, tenantRef);

        await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/roles/{roleRef}/permissions"),
            admin.Token,
            new { Slug = "clients.read", IsDeny = false });

        (await fixture.GetEffectivePermissionsAsync(member.Token, workspaceRef))
            .Should().NotContain("clients.read", "the role is not assigned yet");

        await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/members/{memberRef}/roles/{roleRef}"),
            admin.Token,
            new { WorkspaceRef = (Guid?)null });

        (await fixture.GetEffectivePermissionsAsync(member.Token, workspaceRef))
            .Should().Contain("clients.read");
    }

    [SkippableFact]
    public async Task RPERM_13_RolePermissionsAreUnscoped_AndApplyWhereverAssigned()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef) = await ArrangeAsync();
        var workspaceOne = await fixture.CreateWorkspaceAsync(admin.Token);
        var workspaceTwo = await fixture.CreateWorkspaceAsync(admin.Token);

        var member = await fixture.CreateUserInTenantAsync(admin.Token, tenantRef);
        foreach (var workspace in new[] { workspaceOne, workspaceTwo })
        {
            await fixture.SendAsync(
                HttpMethod.Post,
                ServicesFixture.AuthUrl($"/api/workspaces/{workspace}/members"),
                admin.Token,
                new { Email = member.Email });
        }

        var memberRef = await fixture.FindTenantMemberRefAsync(admin.Token, tenantRef, member.Email);
        var roleRef = await fixture.CreateRoleAsync(admin.Token, tenantRef);

        await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/roles/{roleRef}/permissions"),
            admin.Token,
            new { Slug = "logs.read", IsDeny = false });

        // Assigned tenant-wide, so the role's unscoped permissions land in every workspace.
        await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/members/{memberRef}/roles/{roleRef}"),
            admin.Token,
            new { WorkspaceRef = (Guid?)null });

        (await fixture.GetEffectivePermissionsAsync(member.Token, workspaceOne)).Should().Contain("logs.read");
        (await fixture.GetEffectivePermissionsAsync(member.Token, workspaceTwo)).Should().Contain("logs.read");
    }
}
