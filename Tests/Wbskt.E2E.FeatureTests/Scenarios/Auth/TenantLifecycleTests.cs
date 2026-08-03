using System.Net;
using FluentAssertions;
using Wbskt.E2E.FeatureTests.Fixtures;

namespace Wbskt.E2E.FeatureTests.Scenarios.Auth;

/// <summary>
/// Tenant creation, invitations and offboarding — the sign-up model in which every account owns a
/// tenant and joining someone else's happens only by redeeming an invitation.
///
/// Requires the auth host running against a database with the tenant procedures deployed. Skips
/// gracefully when the hosts are down.
/// </summary>
[Collection(E2ECollection.Name)]
public sealed class TenantLifecycleTests(ServicesFixture fixture)
{
    // ── Registration gives every account a tenant of its own ──────────────────────────────

    [SkippableFact]
    public async Task TEN_01_Registration_CreatesOwnTenantAndWorkspace()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var user = await fixture.CreateUserAsync();

        var tenantRef = await fixture.GetTenantRefAsync(user.Token);
        tenantRef.Should().NotBeEmpty("registration creates a tenant, so no account is left unable to act");

        var workspaces = await fixture.GetWorkspaceRefsAsync(user.Token);
        workspaces.Should().NotBeEmpty("the tenant is created with a default workspace in the same transaction");
    }

    [SkippableFact]
    public async Task TEN_02_NewUser_IsAdministratorOfTheirOwnTenant()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var user = await fixture.CreateUserAsync();
        var tenantRef = await fixture.GetTenantRefAsync(user.Token);

        // Listing roles requires tenant-wide roles.read. If the Admin assignment had been scoped to
        // the workspace instead of the tenant, this would be a 403.
        var response = await fixture.SendAsync(
            HttpMethod.Get, ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/roles"), user.Token);

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "the tenant's creator holds Admin tenant-wide, otherwise nobody could ever administer it");
    }

    [SkippableFact]
    public async Task TEN_03_TwoRegistrations_DoNotShareATenant()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var userA = await fixture.CreateUserAsync();
        var userB = await fixture.CreateUserAsync();

        var tenantA = await fixture.GetTenantRefAsync(userA.Token);
        var tenantB = await fixture.GetTenantRefAsync(userB.Token);

        tenantA.Should().NotBe(tenantB, "sign-up must not drop unrelated accounts into a shared tenant");
    }

    [SkippableFact]
    public async Task TEN_04_MemberList_DoesNotLeakAcrossTenants()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var userA = await fixture.CreateUserAsync();
        var userB = await fixture.CreateUserAsync();
        var tenantA = await fixture.GetTenantRefAsync(userA.Token);

        var found = await fixture.FindTenantMemberRefAsync(userA.Token, tenantA, userB.Email);

        found.Should().BeNull("a tenant lists its own members only");
    }

    [SkippableFact]
    public async Task TEN_05_TenantOfAnotherUser_IsNotAddressable()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var userA = await fixture.CreateUserAsync();
        var userB = await fixture.CreateUserAsync();
        var tenantB = await fixture.GetTenantRefAsync(userB.Token);

        var response = await fixture.SendAsync(
            HttpMethod.Get, ServicesFixture.AuthUrl($"/api/tenants/{tenantB}/members"), userA.Token);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "a tenant the caller is not a member of resolves the same as one that does not exist");
    }

    // ── Invitations are the only way into an existing tenant ──────────────────────────────

    [SkippableFact]
    public async Task TEN_06_InvitedUser_JoinsTheInvitingTenant()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var owner = await fixture.CreateUserAsync();
        var tenantRef = await fixture.GetTenantRefAsync(owner.Token);

        var invited = await fixture.CreateUserInTenantAsync(owner.Token, tenantRef);

        var memberRef = await fixture.FindTenantMemberRefAsync(owner.Token, tenantRef, invited.Email);
        memberRef.Should().NotBeNull("redeeming the invitation joins the tenant that issued it");
    }

    [SkippableFact]
    public async Task TEN_07_InvitationToken_IsSingleUse()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var owner = await fixture.CreateUserAsync();
        var tenantRef = await fixture.GetTenantRefAsync(owner.Token);

        var outsider = await fixture.CreateUserAsync();
        var token = await fixture.InviteAsync(owner.Token, tenantRef, outsider.Email);

        var first = await fixture.AcceptInvitationAsync(outsider.Token, token);
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        var second = await fixture.AcceptInvitationAsync(outsider.Token, token);
        second.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "a redeemed invitation is spent — otherwise the link stays a standing grant of membership");
        (await ServicesFixture.ReadErrorCodeAsync(second)).Should().Be("INVITATION_INVALID");
    }

    [SkippableFact]
    public async Task TEN_08_InvitationAddressedToSomeoneElse_IsRejected()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var owner = await fixture.CreateUserAsync();
        var tenantRef = await fixture.GetTenantRefAsync(owner.Token);

        var intended = await fixture.CreateUserAsync();
        var interloper = await fixture.CreateUserAsync();

        var token = await fixture.InviteAsync(owner.Token, tenantRef, intended.Email);

        var response = await fixture.AcceptInvitationAsync(interloper.Token, token);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "possession of a leaked link must not be enough to join — the invitation names an address");
        (await ServicesFixture.ReadErrorCodeAsync(response)).Should().Be("INVITATION_INVALID");
    }

    [SkippableFact]
    public async Task TEN_09_RevokedInvitation_CannotBeRedeemed()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var owner = await fixture.CreateUserAsync();
        var tenantRef = await fixture.GetTenantRefAsync(owner.Token);

        var outsider = await fixture.CreateUserAsync();
        var token = await fixture.InviteAsync(owner.Token, tenantRef, outsider.Email);

        var pending = await fixture.GetInvitationsAsync(owner.Token, tenantRef);
        var invitationRef = pending.Should().ContainSingle(i => i.Email == outsider.Email).Subject.RefId;

        var revoke = await fixture.SendAsync(
            HttpMethod.Delete,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/invitations/{invitationRef}"),
            owner.Token);
        revoke.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var response = await fixture.AcceptInvitationAsync(outsider.Token, token);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [SkippableFact]
    public async Task TEN_10_UnknownInvitationToken_IsRejected()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var user = await fixture.CreateUserAsync();

        var response = await fixture.AcceptInvitationAsync(user.Token, "not-a-real-invitation-token");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ServicesFixture.ReadErrorCodeAsync(response)).Should().Be("INVITATION_INVALID",
            "an unknown token is reported exactly as a spent one, so the endpoint cannot be probed");
    }

    // ── The workspace add-member path no longer widens the tenant ─────────────────────────

    [SkippableFact]
    public async Task TEN_11_AddingAnOutsiderToAWorkspace_IsRejected()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var owner = await fixture.CreateUserAsync();
        var workspaceRef = (await fixture.GetWorkspaceRefsAsync(owner.Token)).First();

        var outsider = await fixture.CreateUserAsync();

        var response = await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/workspaces/{workspaceRef}/members"),
            owner.Token,
            new { Email = outsider.Email });

        // The regression this pins: adding by email used to insert the missing TenantMembers row,
        // so any workspace administrator could pull any account in the system into their tenant.
        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "a user outside the tenant is reported exactly as an unregistered address, so the "
            + "endpoint cannot be used to test whether an email has an account");
    }

    [SkippableFact]
    public async Task TEN_12_InvitedMember_CanThenBeAddedToAWorkspace()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var owner = await fixture.CreateUserAsync();
        var tenantRef = await fixture.GetTenantRefAsync(owner.Token);
        var workspaceRef = (await fixture.GetWorkspaceRefsAsync(owner.Token)).First();

        var member = await fixture.CreateUserInTenantAsync(owner.Token, tenantRef);

        var response = await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/workspaces/{workspaceRef}/members"),
            owner.Token,
            new { Email = member.Email });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent,
            "tenant membership is the prerequisite the guard enforces, and this user has it");
    }

    // ── Offboarding ───────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task TEN_13_RemovingAMember_TakesThemOutOfTheTenant()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var owner = await fixture.CreateUserAsync();
        var tenantRef = await fixture.GetTenantRefAsync(owner.Token);

        var member = await fixture.CreateUserInTenantAsync(owner.Token, tenantRef);
        var memberRef = (await fixture.FindTenantMemberRefAsync(owner.Token, tenantRef, member.Email))!.Value;

        var response = await fixture.SendAsync(
            HttpMethod.Delete,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/members/{memberRef}"),
            owner.Token);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await fixture.FindTenantMemberRefAsync(owner.Token, tenantRef, member.Email))
            .Should().BeNull("removal takes the membership row with it");
    }

    [SkippableFact]
    public async Task TEN_14_RemovedMember_KeepsTheirOwnTenant()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var owner = await fixture.CreateUserAsync();
        var tenantRef = await fixture.GetTenantRefAsync(owner.Token);

        var member = await fixture.CreateUserInTenantAsync(owner.Token, tenantRef);
        var memberRef = (await fixture.FindTenantMemberRefAsync(owner.Token, tenantRef, member.Email))!.Value;

        await fixture.SendAsync(
            HttpMethod.Delete,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/members/{memberRef}"),
            owner.Token);

        // The reason registration creates a tenant even for an invited user: being offboarded from
        // the tenant that invited you must not leave an account that belongs nowhere.
        var ownTenant = await fixture.GetTenantRefAsync(member.Token);
        ownTenant.Should().NotBe(tenantRef);
    }

    [SkippableFact]
    public async Task TEN_15_NonAdministrator_CannotRemoveAMember()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var owner = await fixture.CreateUserAsync();
        var tenantRef = await fixture.GetTenantRefAsync(owner.Token);

        var member = await fixture.CreateUserInTenantAsync(owner.Token, tenantRef);
        var ownerRef = (await fixture.FindTenantMemberRefAsync(owner.Token, tenantRef, owner.Email))!.Value;

        var response = await fixture.SendAsync(
            HttpMethod.Delete,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/members/{ownerRef}"),
            member.Token);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "the member was invited without a role, so they hold no users.manage");

        // Note the last-administrator guard (THROW 50008) is deliberately *not* what rejects this.
        // It cannot be reached along the ordinary path: the caller must hold tenant-wide
        // users.manage and cannot target themselves, so a remaining administrator is implied. It
        // fires only where the guard undercounts — an administrator whose users.manage comes via a
        // group — and as a backstop for callers that are not this endpoint.
    }

    [SkippableFact]
    public async Task TEN_16_RemovingYourself_IsRejected()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var owner = await fixture.CreateUserAsync();
        var tenantRef = await fixture.GetTenantRefAsync(owner.Token);
        var ownerRef = (await fixture.FindTenantMemberRefAsync(owner.Token, tenantRef, owner.Email))!.Value;

        var response = await fixture.SendAsync(
            HttpMethod.Delete,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/members/{ownerRef}"),
            owner.Token);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "workspaces transfer to the caller, so removing yourself has no recipient");
        (await ServicesFixture.ReadErrorCodeAsync(response)).Should().Be("CANNOT_REMOVE_SELF");
    }

    // ── Tenant creation and rename ────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task TEN_17_UserCanCreateAdditionalTenants()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var user = await fixture.CreateUserAsync();

        var response = await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl("/api/tenants"),
            user.Token,
            new { Name = $"e2e-tenant-{Guid.NewGuid():N}", Description = "Created by E2E." });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var tenants = await fixture.GetTenantsAsync(user.Token);
        tenants.Should().HaveCountGreaterThan(1, "the tenant list is genuinely multi-valued");
    }

    [SkippableFact]
    public async Task TEN_18_TenantNamesNeedNotBeUnique()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var user = await fixture.CreateUserAsync();
        var name = $"e2e-shared-name-{Guid.NewGuid():N}";

        var first = await fixture.SendAsync(
            HttpMethod.Post, ServicesFixture.AuthUrl("/api/tenants"), user.Token, new { Name = name, Description = (string?)null });
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        var second = await fixture.SendAsync(
            HttpMethod.Post, ServicesFixture.AuthUrl("/api/tenants"), user.Token, new { Name = name, Description = (string?)null });

        // Sign-up names a tenant after the user, so a global uniqueness constraint would make
        // registration fail on a name the user never chose.
        second.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [SkippableFact]
    public async Task TEN_19_TenantCanBeRenamed()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var user = await fixture.CreateUserAsync();
        var tenantRef = await fixture.GetTenantRefAsync(user.Token);
        var newName = $"e2e-renamed-{Guid.NewGuid():N}";

        var response = await fixture.SendAsync(
            HttpMethod.Put,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}"),
            user.Token,
            new { Name = newName, Description = "Renamed by E2E." });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var tenants = await fixture.GetTenantsAsync(user.Token);
        tenants.Should().Contain(t => t.RefId == tenantRef && t.Name == newName);
    }

    [SkippableFact]
    public async Task TEN_20_InvitingAnExistingMember_IsRejected()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var owner = await fixture.CreateUserAsync();
        var tenantRef = await fixture.GetTenantRefAsync(owner.Token);

        var response = await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/invitations"),
            owner.Token,
            new { Email = owner.Email, RoleRef = (Guid?)null });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ServicesFixture.ReadErrorCodeAsync(response)).Should().Be("AUTH_ALREADY_MEMBER");
    }
}
