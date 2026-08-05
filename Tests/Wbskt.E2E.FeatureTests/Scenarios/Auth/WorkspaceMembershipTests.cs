using System.Net;
using FluentAssertions;
using Wbskt.E2E.FeatureTests.Fixtures;

namespace Wbskt.E2E.FeatureTests.Scenarios.Auth;

/// <summary>
/// Section 11 of Docs/Auth.Host.E2E.Scenarios.md — <c>/api/workspaces/{ref}/members</c>.
///
/// Adding a member is tenant-bounded: the endpoint takes an email but will only admit an account
/// that already belongs to the workspace's tenant. Before that, anyone holding <c>users.manage</c>
/// in a single workspace could capture any account in the system knowing only its address, and
/// distinguish a registered address from an unregistered one. WS_MEM_06 and WS_MEM_06b pin both
/// halves of that closure.
///
/// Requires the auth host running. Skips gracefully when it is down.
/// </summary>
[Collection(E2ECollection.Name)]
public sealed class WorkspaceMembershipTests(ServicesFixture fixture)
{
    /// <summary>An owner with a tenant, a workspace, and one invited tenant member.</summary>
    private async Task<(ServicesFixture.TestUser Owner, Guid TenantRef, Guid WorkspaceRef, ServicesFixture.TestUser Member)> ArrangeAsync()
    {
        var owner = await fixture.CreateUserAsync();
        var tenantRef = await fixture.GetTenantRefAsync(owner.Token);
        var workspaceRef = await fixture.CreateWorkspaceAsync(owner.Token);
        var member = await fixture.CreateUserInTenantAsync(owner.Token, tenantRef);

        return (owner, tenantRef, workspaceRef, member);
    }

    private Task<HttpResponseMessage> AddMemberAsync(string token, Guid workspaceRef, string email) =>
        fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/workspaces/{workspaceRef}/members"),
            token,
            new { Email = email });

    // ── Adding ────────────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task WS_MEM_01_AddTenantMember_Succeeds()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (owner, _, workspaceRef, member) = await ArrangeAsync();

        var response = await AddMemberAsync(owner.Token, workspaceRef, member.Email);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [SkippableFact]
    public async Task WS_MEM_03_AddedMember_CanResolveTheWorkspace()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (owner, _, workspaceRef, member) = await ArrangeAsync();

        (await fixture.ResolveWorkspaceAsync(member.Token, workspaceRef)).StatusCode
            .Should().Be(HttpStatusCode.Forbidden, "not a member yet");

        await AddMemberAsync(owner.Token, workspaceRef, member.Email);

        (await fixture.ResolveWorkspaceAsync(member.Token, workspaceRef)).StatusCode
            .Should().Be(HttpStatusCode.OK);
    }

    [SkippableFact]
    public async Task WS_MEM_04_AddedMember_SeesItInTheirWorkspaceList()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (owner, _, workspaceRef, member) = await ArrangeAsync();
        await AddMemberAsync(owner.Token, workspaceRef, member.Email);

        (await fixture.GetWorkspaceRefsAsync(member.Token)).Should().Contain(workspaceRef);
    }

    [SkippableFact]
    public async Task WS_MEM_05_AddingTwice_IsIdempotent()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (owner, _, workspaceRef, member) = await ArrangeAsync();

        await AddMemberAsync(owner.Token, workspaceRef, member.Email);
        var second = await AddMemberAsync(owner.Token, workspaceRef, member.Email);

        second.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var (itemCount, _) = await fixture.GetPageAsync(
            ServicesFixture.AuthUrl($"/api/workspaces/{workspaceRef}/members?take=200"), owner.Token);

        itemCount.Should().Be(2, "the owner plus one member, added once");
    }

    [SkippableFact]
    public async Task WS_MEM_06_AddingAnUnregisteredAddress_Returns404()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (owner, _, workspaceRef, _) = await ArrangeAsync();

        var response = await AddMemberAsync(
            owner.Token, workspaceRef, $"nobody-{Guid.NewGuid():N}@test.local");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ServicesFixture.ReadErrorCodeAsync(response)).Should().Be("USER_NOT_FOUND");
    }

    [SkippableFact]
    public async Task WS_MEM_06b_OutsiderAndUnknownAddress_AreIndistinguishable()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (owner, _, workspaceRef, _) = await ArrangeAsync();

        // A real account, registered, but in a tenant of its own.
        var outsider = await fixture.CreateUserAsync();

        var registeredOutsider = await AddMemberAsync(owner.Token, workspaceRef, outsider.Email);
        var neverRegistered = await AddMemberAsync(
            owner.Token, workspaceRef, $"nobody-{Guid.NewGuid():N}@test.local");

        // Identical answers, or this endpoint tells any workspace administrator whether an
        // arbitrary email has an account on the platform.
        registeredOutsider.StatusCode.Should().Be(neverRegistered.StatusCode);
        (await ServicesFixture.ReadErrorCodeAsync(registeredOutsider))
            .Should().Be(await ServicesFixture.ReadErrorCodeAsync(neverRegistered));
    }

    [SkippableFact]
    public async Task WS_MEM_07_AddByAMemberWithoutUsersManage_Returns403()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (owner, tenantRef, workspaceRef, member) = await ArrangeAsync();
        await AddMemberAsync(owner.Token, workspaceRef, member.Email);

        var another = await fixture.CreateUserInTenantAsync(owner.Token, tenantRef);

        var response = await AddMemberAsync(member.Token, workspaceRef, another.Email);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ServicesFixture.ReadErrorCodeAsync(response)).Should().Be("PERMISSION_UNAUTHORIZED");
    }

    [SkippableFact]
    public async Task WS_MEM_08_AddToAWorkspaceTheCallerIsNotIn_Returns403()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (_, _, workspaceRef, member) = await ArrangeAsync();

        var outsider = await fixture.CreateUserAsync();

        var response = await AddMemberAsync(outsider.Token, workspaceRef, member.Email);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [SkippableFact]
    public async Task WS_MEM_09_MalformedEmail_IsRejectedByValidation()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (owner, _, workspaceRef, _) = await ArrangeAsync();

        var response = await AddMemberAsync(owner.Token, workspaceRef, "not-an-email");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [SkippableFact]
    public async Task WS_MEM_10_AddWithUnknownWorkspaceRef_Returns403()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (owner, _, _, member) = await ArrangeAsync();

        var response = await AddMemberAsync(owner.Token, Guid.NewGuid(), member.Email);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ServicesFixture.ReadErrorCodeAsync(response)).Should().Be("WORKSPACE_NOT_FOUND");
    }

    // ── Listing ───────────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task WS_MEM_11_ListMembers_IncludesOwnerAndAddedMembers()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (owner, _, workspaceRef, member) = await ArrangeAsync();
        await AddMemberAsync(owner.Token, workspaceRef, member.Email);

        var (itemCount, totalCount) = await fixture.GetPageAsync(
            ServicesFixture.AuthUrl($"/api/workspaces/{workspaceRef}/members?take=200"), owner.Token);

        itemCount.Should().Be(2);
        totalCount.Should().Be(2, "X-Total-Count must describe the collection");
    }

    [SkippableFact]
    public async Task WS_MEM_12_TakeOne_ReturnsOnePageButTheRealTotal()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (owner, _, workspaceRef, member) = await ArrangeAsync();
        await AddMemberAsync(owner.Token, workspaceRef, member.Email);

        var (itemCount, totalCount) = await fixture.GetPageAsync(
            ServicesFixture.AuthUrl($"/api/workspaces/{workspaceRef}/members?skip=0&take=1"), owner.Token);

        itemCount.Should().Be(1);
        totalCount.Should().Be(2);
    }

    [SkippableFact]
    public async Task WS_MEM_13_TakeZero_IsClampedNotRejected()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (owner, _, workspaceRef, _) = await ArrangeAsync();

        // Clamped rather than rejected: an out-of-range page size is not worth failing a read over.
        var (itemCount, _) = await fixture.GetPageAsync(
            ServicesFixture.AuthUrl($"/api/workspaces/{workspaceRef}/members?take=0"), owner.Token);

        itemCount.Should().Be(1);
    }

    [SkippableFact]
    public async Task WS_MEM_14_OversizedTake_IsClampedToTheMaximum()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (owner, _, workspaceRef, _) = await ArrangeAsync();

        // The clamp is what stops a caller pulling the whole table in one request.
        var (itemCount, _) = await fixture.GetPageAsync(
            ServicesFixture.AuthUrl($"/api/workspaces/{workspaceRef}/members?take=99999"), owner.Token);

        itemCount.Should().BeLessThanOrEqualTo(200);
    }

    [SkippableFact]
    public async Task WS_MEM_15_NegativeSkip_IsTreatedAsZero()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (owner, _, workspaceRef, _) = await ArrangeAsync();

        var (itemCount, _) = await fixture.GetPageAsync(
            ServicesFixture.AuthUrl($"/api/workspaces/{workspaceRef}/members?skip=-5&take=10"), owner.Token);

        itemCount.Should().BeGreaterThan(0);
    }

    [SkippableFact]
    public async Task WS_MEM_16_SkipBeyondTheEnd_ReturnsEmptyWithTheRealTotal()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (owner, _, workspaceRef, _) = await ArrangeAsync();

        var (itemCount, totalCount) = await fixture.GetPageAsync(
            ServicesFixture.AuthUrl($"/api/workspaces/{workspaceRef}/members?skip=500&take=10"), owner.Token);

        itemCount.Should().Be(0);
        totalCount.Should().BeGreaterThan(0, "the total describes the collection, not the window");
    }

    [SkippableFact]
    public async Task WS_MEM_17_ListByAMemberWithoutUsersRead_Returns403()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (owner, _, workspaceRef, member) = await ArrangeAsync();
        await AddMemberAsync(owner.Token, workspaceRef, member.Email);

        var response = await fixture.SendAsync(
            HttpMethod.Get,
            ServicesFixture.AuthUrl($"/api/workspaces/{workspaceRef}/members"),
            member.Token);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [SkippableFact]
    public async Task WS_MEM_23_MemberListExposesReferencesNotIds()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (owner, _, workspaceRef, _) = await ArrangeAsync();

        var response = await fixture.SendAsync(
            HttpMethod.Get,
            ServicesFixture.AuthUrl($"/api/workspaces/{workspaceRef}/members"),
            owner.Token);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();

        body.Should().Contain("refId");
        body.Should().NotContain("\"id\"", "the internal integer ID must never cross the API boundary");
    }

    // ── Removing ──────────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task WS_MEM_18_RemoveMember_RevokesTheirAccess()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (owner, tenantRef, workspaceRef, member) = await ArrangeAsync();
        await AddMemberAsync(owner.Token, workspaceRef, member.Email);

        var memberRef = await fixture.FindTenantMemberRefAsync(owner.Token, tenantRef, member.Email);
        memberRef.Should().NotBeNull();

        var response = await fixture.SendAsync(
            HttpMethod.Delete,
            ServicesFixture.AuthUrl($"/api/workspaces/{workspaceRef}/members/{memberRef}"),
            owner.Token);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await fixture.ResolveWorkspaceAsync(member.Token, workspaceRef)).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
    }

    [SkippableFact]
    public async Task WS_MEM_19_RemovingTheOwner_IsRejected()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (owner, tenantRef, workspaceRef, _) = await ArrangeAsync();

        var ownerRef = await fixture.FindTenantMemberRefAsync(owner.Token, tenantRef, owner.Email);
        ownerRef.Should().NotBeNull();

        var response = await fixture.SendAsync(
            HttpMethod.Delete,
            ServicesFixture.AuthUrl($"/api/workspaces/{workspaceRef}/members/{ownerRef}"),
            owner.Token);

        // A workspace with no owner has no route back to being administered.
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ServicesFixture.ReadErrorCodeAsync(response)).Should().Be("WORKSPACE_OWNER_PROTECTED");
    }

    [SkippableFact]
    public async Task WS_MEM_20_RemovingAnUnknownUserRef_Returns403()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (owner, _, workspaceRef, _) = await ArrangeAsync();

        var response = await fixture.SendAsync(
            HttpMethod.Delete,
            ServicesFixture.AuthUrl($"/api/workspaces/{workspaceRef}/members/{Guid.NewGuid()}"),
            owner.Token);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ServicesFixture.ReadErrorCodeAsync(response)).Should().Be("USER_NOT_FOUND");
    }

    [SkippableFact]
    public async Task WS_MEM_21_RemoveByAMemberWithoutUsersManage_Returns403()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (owner, tenantRef, workspaceRef, member) = await ArrangeAsync();
        await AddMemberAsync(owner.Token, workspaceRef, member.Email);

        var another = await fixture.CreateUserInTenantAsync(owner.Token, tenantRef);
        await AddMemberAsync(owner.Token, workspaceRef, another.Email);
        var anotherRef = await fixture.FindTenantMemberRefAsync(owner.Token, tenantRef, another.Email);

        var response = await fixture.SendAsync(
            HttpMethod.Delete,
            ServicesFixture.AuthUrl($"/api/workspaces/{workspaceRef}/members/{anotherRef}"),
            member.Token);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [SkippableFact]
    public async Task WS_MEM_22_RemovalDropsAssignmentsScopedToTheWorkspace()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (owner, tenantRef, workspaceRef, member) = await ArrangeAsync();
        await AddMemberAsync(owner.Token, workspaceRef, member.Email);

        var memberRef = await fixture.FindTenantMemberRefAsync(owner.Token, tenantRef, member.Email);
        var roleRef = await fixture.CreateRoleAsync(owner.Token, tenantRef);

        await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/roles/{roleRef}/permissions"),
            owner.Token,
            new { Slug = "logs.read", IsDeny = false });

        await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/members/{memberRef}/roles/{roleRef}"),
            owner.Token,
            new { WorkspaceRef = (Guid?)workspaceRef });

        (await fixture.GetEffectivePermissionsAsync(member.Token, workspaceRef))
            .Should().Contain("logs.read");

        await fixture.SendAsync(
            HttpMethod.Delete,
            ServicesFixture.AuthUrl($"/api/workspaces/{workspaceRef}/members/{memberRef}"),
            owner.Token);

        // Re-added, the workspace-scoped assignment must not come back with them.
        await AddMemberAsync(owner.Token, workspaceRef, member.Email);

        (await fixture.GetEffectivePermissionsAsync(member.Token, workspaceRef))
            .Should().NotContain("logs.read", "removal clears assignments scoped to the workspace");
    }
}
