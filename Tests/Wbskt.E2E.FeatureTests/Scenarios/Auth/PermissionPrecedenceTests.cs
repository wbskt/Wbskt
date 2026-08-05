using FluentAssertions;
using Wbskt.E2E.FeatureTests.Fixtures;

namespace Wbskt.E2E.FeatureTests.Scenarios.Auth;

/// <summary>
/// Section 14 of Docs/Auth.Host.E2E.Scenarios.md — the effective permission set, observed through
/// <c>POST /api/workspaces/resolve</c>.
///
/// The order <c>Permission_EffectiveSet</c> implements is
/// <b>User-Deny &gt; User-Allow &gt; Role-Deny &gt; Role-Allow &gt; default deny</b>. Every rung
/// matters: the two deny rungs are how an administrator carves an exception without rewriting a
/// role for everyone, and the default is what makes an unassigned permission absent rather than
/// present. These are the arms not already covered in passing by WS_RES_12, MEM_25, GRL_05 and
/// MEM_18/19.
///
/// Requires the auth host running. Skips gracefully when it is down.
/// </summary>
[Collection(E2ECollection.Name)]
public sealed class PermissionPrecedenceTests(ServicesFixture fixture)
{
    private const string Slug = "logs.read";

    /// <summary>An administrator, a workspace, and a member of it holding nothing.</summary>
    private async Task<(ServicesFixture.TestUser Admin, Guid TenantRef, ServicesFixture.TestUser Member, Guid MemberRef, Guid WorkspaceRef)> ArrangeAsync()
    {
        var admin = await fixture.CreateUserAsync();
        var tenantRef = await fixture.GetTenantRefAsync(admin.Token);
        var workspaceRef = await fixture.CreateWorkspaceAsync(admin.Token);

        var member = await fixture.CreateUserInTenantAsync(admin.Token, tenantRef);
        await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/workspaces/{workspaceRef}/members"),
            admin.Token,
            new { Email = member.Email });

        var memberRef = await fixture.FindTenantMemberRefAsync(admin.Token, tenantRef, member.Email);
        memberRef.Should().NotBeNull();

        return (admin, tenantRef, member, memberRef!.Value, workspaceRef);
    }

    /// <summary>Creates a role carrying one entry for <see cref="Slug"/>, allowed or denied.</summary>
    private async Task<Guid> CreateRoleAsync(string adminToken, Guid tenantRef, bool isDeny)
    {
        var roleRef = await fixture.CreateRoleAsync(adminToken, tenantRef);

        await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/roles/{roleRef}/permissions"),
            adminToken,
            new { Slug, IsDeny = isDeny });

        return roleRef;
    }

    private Task AssignRoleAsync(string adminToken, Guid tenantRef, Guid memberRef, Guid roleRef) =>
        fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/members/{memberRef}/roles/{roleRef}"),
            adminToken,
            new { WorkspaceRef = (Guid?)null });

    private Task GrantDirectAsync(string adminToken, Guid tenantRef, Guid memberRef, bool isDeny) =>
        fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/members/{memberRef}/permissions"),
            adminToken,
            new { Slug, IsDeny = isDeny, WorkspaceRef = (Guid?)null });

    [SkippableFact]
    public async Task PREC_01_NoAssignments_MeansAbsent()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (_, _, member, _, workspaceRef) = await ArrangeAsync();

        // The default is deny, not allow: membership gets you through the gate holding nothing.
        (await fixture.GetEffectivePermissionsAsync(member.Token, workspaceRef)).Should().NotContain(Slug);
    }

    [SkippableFact]
    public async Task PREC_02_RoleAllowAlone_MeansPresent()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef, member, memberRef, workspaceRef) = await ArrangeAsync();

        var roleRef = await CreateRoleAsync(admin.Token, tenantRef, isDeny: false);
        await AssignRoleAsync(admin.Token, tenantRef, memberRef, roleRef);

        (await fixture.GetEffectivePermissionsAsync(member.Token, workspaceRef)).Should().Contain(Slug);
    }

    [SkippableFact]
    public async Task PREC_03_RoleDenyAlone_MeansAbsent()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef, member, memberRef, workspaceRef) = await ArrangeAsync();

        var roleRef = await CreateRoleAsync(admin.Token, tenantRef, isDeny: true);
        await AssignRoleAsync(admin.Token, tenantRef, memberRef, roleRef);

        (await fixture.GetEffectivePermissionsAsync(member.Token, workspaceRef)).Should().NotContain(Slug);
    }

    [SkippableFact]
    public async Task PREC_04_RoleDenyBeatsRoleAllowAcrossTwoRoles()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef, member, memberRef, workspaceRef) = await ArrangeAsync();

        var allowRole = await CreateRoleAsync(admin.Token, tenantRef, isDeny: false);
        var denyRole = await CreateRoleAsync(admin.Token, tenantRef, isDeny: true);

        await AssignRoleAsync(admin.Token, tenantRef, memberRef, allowRole);
        await AssignRoleAsync(admin.Token, tenantRef, memberRef, denyRole);

        // Within the role level, deny wins. Otherwise a deny role could be neutralised by handing
        // someone any other role that happens to allow the same slug.
        (await fixture.GetEffectivePermissionsAsync(member.Token, workspaceRef)).Should().NotContain(Slug);
    }

    [SkippableFact]
    public async Task PREC_05_UserAllowBeatsRoleDeny()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef, member, memberRef, workspaceRef) = await ArrangeAsync();

        var denyRole = await CreateRoleAsync(admin.Token, tenantRef, isDeny: true);
        await AssignRoleAsync(admin.Token, tenantRef, memberRef, denyRole);

        (await fixture.GetEffectivePermissionsAsync(member.Token, workspaceRef)).Should().NotContain(Slug);

        await GrantDirectAsync(admin.Token, tenantRef, memberRef, isDeny: false);

        // The user level outranks the role level in both directions — this is the exception-granting
        // half, the counterpart to MEM_25.
        (await fixture.GetEffectivePermissionsAsync(member.Token, workspaceRef)).Should().Contain(Slug);
    }

    [SkippableFact]
    public async Task PREC_07_UserDenyBeatsUserAllow()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef, member, memberRef, workspaceRef) = await ArrangeAsync();

        await GrantDirectAsync(admin.Token, tenantRef, memberRef, isDeny: false);
        (await fixture.GetEffectivePermissionsAsync(member.Token, workspaceRef)).Should().Contain(Slug);

        // A MERGE on the same scope, so this replaces the allow rather than adding a second row —
        // and the result is a denial either way, since deny wins within a level.
        await GrantDirectAsync(admin.Token, tenantRef, memberRef, isDeny: true);

        (await fixture.GetEffectivePermissionsAsync(member.Token, workspaceRef)).Should().NotContain(Slug);
    }

    [SkippableFact]
    public async Task PREC_10_GroupDerivedRoleBehavesLikeADirectOne()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef, member, memberRef, workspaceRef) = await ArrangeAsync();

        var groupRef = await fixture.CreateGroupAsync(admin.Token, tenantRef);
        var roleRef = await CreateRoleAsync(admin.Token, tenantRef, isDeny: false);

        await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/groups/{groupRef}/roles/{roleRef}"),
            admin.Token,
            new { WorkspaceRef = (Guid?)null });

        await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/members/{memberRef}/groups/{groupRef}"),
            admin.Token);

        (await fixture.GetEffectivePermissionsAsync(member.Token, workspaceRef)).Should().Contain(Slug);
    }

    [SkippableFact]
    public async Task PREC_10b_UserDenyBeatsAGroupDerivedAllow()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef, member, memberRef, workspaceRef) = await ArrangeAsync();

        var groupRef = await fixture.CreateGroupAsync(admin.Token, tenantRef);
        var roleRef = await CreateRoleAsync(admin.Token, tenantRef, isDeny: false);

        await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/groups/{groupRef}/roles/{roleRef}"),
            admin.Token,
            new { WorkspaceRef = (Guid?)null });

        await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/members/{memberRef}/groups/{groupRef}"),
            admin.Token);

        await GrantDirectAsync(admin.Token, tenantRef, memberRef, isDeny: true);

        // Group membership collapses into the role level, so it loses to a user-level entry the same
        // way a directly assigned role does. Excluding one person from a group-wide grant must not
        // require removing them from the group.
        (await fixture.GetEffectivePermissionsAsync(member.Token, workspaceRef)).Should().NotContain(Slug);
    }

    [SkippableFact]
    public async Task PREC_13_RemovingTheOnlySource_RemovesTheSlug()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef, member, memberRef, workspaceRef) = await ArrangeAsync();

        var roleRef = await CreateRoleAsync(admin.Token, tenantRef, isDeny: false);
        await AssignRoleAsync(admin.Token, tenantRef, memberRef, roleRef);

        (await fixture.GetEffectivePermissionsAsync(member.Token, workspaceRef)).Should().Contain(Slug);

        await fixture.SendAsync(
            HttpMethod.Delete,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/members/{memberRef}/roles/{roleRef}"),
            admin.Token);

        (await fixture.GetEffectivePermissionsAsync(member.Token, workspaceRef)).Should().NotContain(Slug);
    }

    [SkippableFact]
    public async Task PREC_13b_RemovingOneOfTwoSources_LeavesTheOther()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (admin, tenantRef, member, memberRef, workspaceRef) = await ArrangeAsync();

        var firstRole = await CreateRoleAsync(admin.Token, tenantRef, isDeny: false);
        var secondRole = await CreateRoleAsync(admin.Token, tenantRef, isDeny: false);

        await AssignRoleAsync(admin.Token, tenantRef, memberRef, firstRole);
        await AssignRoleAsync(admin.Token, tenantRef, memberRef, secondRole);

        await fixture.SendAsync(
            HttpMethod.Delete,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/members/{memberRef}/roles/{firstRole}"),
            admin.Token);

        // Allows union across roles, so one grant surviving is enough.
        (await fixture.GetEffectivePermissionsAsync(member.Token, workspaceRef)).Should().Contain(Slug);
    }
}
