using System.Net;
using FluentAssertions;
using Wbskt.Api.Client;
using Wbskt.Auth.Models;
using Wbskt.E2E.FeatureTests.Fixtures;
using Wbskt.Management.Models;
using Wbskt.Models.Admin;
using Wbskt.Models.Billing;

namespace Wbskt.E2E.FeatureTests.Scenarios.Admin;

/// <summary>
/// The changes the platform admin host (wbskt/Wbskt.Admin) makes for a staff member, called as it calls
/// them: on the auth and management hosts' <c>/api/internal/admin</c> routes with the admin key, never
/// through a user's token. Each change takes effect where a user would see it, and the same request sent
/// again (same idempotency key) answers the first call's audit row without changing anything twice.
///
/// Requires the auth and management hosts running with <c>Internal:AdminApiKey</c> set, as start-hosts.sh
/// does. Skips when they are down or have no key.
/// </summary>
[Collection(AuthCollection.Name)]
public sealed class StaffActionsE2ETests(ServicesFixture fixture)
{
    private static string Admin(string path) => ServicesFixture.AuthUrl($"/api/internal/admin{path}");

    private static StaffChange Staff(Guid? key = null) =>
        new(Guid.NewGuid(), "e2e-staff", "Staff action from an E2E scenario", key ?? Guid.NewGuid());

    private async Task SkipUnlessAdminKeyAsync()
    {
        using var probe = await fixture.SendAdminAsync(HttpMethod.Get, Admin($"/tenants/{Guid.NewGuid()}/limits"));
        Skip.If(probe.StatusCode == HttpStatusCode.NotFound && await ServicesFixture.ReadErrorCodeAsync(probe) is null,
            "The auth host has no Internal:AdminApiKey — skipping.");
    }

    private async Task<int> RevisionAsync(Guid tenantRef)
    {
        using var response = await fixture.SendAdminAsync(HttpMethod.Get, Admin($"/tenants/{tenantRef}/limits"));
        return (await WbsktApiClient.ReadAsync<AdminTenantLimitsResponse>(response)).Plan.PlanRevision;
    }

    private static Task<StaffActionResponse> ReadAsync(HttpResponseMessage response) => WbsktApiClient.ReadAsync<StaffActionResponse>(response);

    [SkippableFact]
    public async Task ADM_01_A_staff_plan_takes_effect_and_a_repeat_replays_it()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        await SkipUnlessAdminKeyAsync();
        var user = await fixture.CreateUserAsync();
        Guid tenantRef = await fixture.GetTenantRefAsync(user.Token);
        var request = new AdminSetTenantPlanRequest(Staff(), PlanCode.Pro, null, await RevisionAsync(tenantRef), DateTimeOffset.UtcNow.AddDays(30));

        var first = await ReadAsync(await fixture.SendAdminAsync(HttpMethod.Put, Admin($"/tenants/{tenantRef}/plan"), request));
        var again = await ReadAsync(await fixture.SendAdminAsync(HttpMethod.Put, Admin($"/tenants/{tenantRef}/plan"), request));

        first.Replayed.Should().BeFalse();
        again.Should().Be(first with { Replayed = true });
        (await fixture.Auth.As(user.Token).GetTenantUsageAsync(tenantRef)).Plan.Should().Be(PlanCode.Pro);
    }

    [SkippableFact]
    public async Task ADM_02_Limits_change_alone_and_a_stale_revision_is_refused()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        await SkipUnlessAdminKeyAsync();
        var user = await fixture.CreateUserAsync();
        Guid tenantRef = await fixture.GetTenantRefAsync(user.Token);
        int revision = await RevisionAsync(tenantRef);

        using var set = await fixture.SendAdminAsync(HttpMethod.Put, Admin($"/tenants/{tenantRef}/limits"),
            new AdminSetTenantLimitsRequest(Staff(), new PlanLimitOverrides { Members = 7 }, revision));
        using var stale = await fixture.SendAdminAsync(HttpMethod.Put, Admin($"/tenants/{tenantRef}/limits"),
            new AdminSetTenantLimitsRequest(Staff(), new PlanLimitOverrides { Members = 9 }, revision));

        set.StatusCode.Should().Be(HttpStatusCode.OK);
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ServicesFixture.ReadErrorCodeAsync(stale)).Should().Be("TENANT_PLAN_CHANGED");
        var usage = await fixture.Auth.As(user.Token).GetTenantUsageAsync(tenantRef);
        usage.Plan.Should().Be(PlanCode.Free);
        usage.Members.Limit.Should().Be(7);
    }

    [SkippableFact]
    public async Task ADM_03_A_staff_plan_ends_and_billing_puts_the_tenant_back_on_free()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        await SkipUnlessAdminKeyAsync();
        var user = await fixture.CreateUserAsync();
        Guid tenantRef = await fixture.GetTenantRefAsync(user.Token);
        using var plan = await fixture.SendAdminAsync(HttpMethod.Put, Admin($"/tenants/{tenantRef}/plan"),
            new AdminSetTenantPlanRequest(Staff(), PlanCode.Team, null, await RevisionAsync(tenantRef), DateTimeOffset.UtcNow.AddDays(30)));
        plan.StatusCode.Should().Be(HttpStatusCode.OK);

        using var end = await fixture.SendAdminAsync(HttpMethod.Post, Admin($"/tenants/{tenantRef}/plan/end"),
            new AdminEndStaffPlanRequest(Staff(), await RevisionAsync(tenantRef)));

        end.StatusCode.Should().Be(HttpStatusCode.OK);
        (await fixture.Auth.As(user.Token).GetTenantUsageAsync(tenantRef)).Plan.Should().Be(PlanCode.Free);
    }

    [SkippableFact]
    public async Task ADM_04_Credits_are_given_once_however_often_the_request_is_sent_and_can_be_revoked()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        await SkipUnlessAdminKeyAsync();
        var user = await fixture.CreateUserAsync();
        Guid tenantRef = await fixture.GetTenantRefAsync(user.Token);
        var key = Guid.NewGuid();
        var grant = new AdminGrantCreditsRequest(Staff(key), 1500m, DateTimeOffset.UtcNow.AddDays(60));

        var first = await ReadAsync(await fixture.SendAdminAsync(HttpMethod.Post, Admin($"/tenants/{tenantRef}/credit-grants"), grant));
        var again = await ReadAsync(await fixture.SendAdminAsync(HttpMethod.Post, Admin($"/tenants/{tenantRef}/credit-grants"), grant));
        using var revoke = await fixture.SendAdminAsync(HttpMethod.Post, Admin($"/tenants/{tenantRef}/credit-grants/{key}/revoke"),
            new AdminRevokeCreditGrantRequest(Staff()));

        again.Should().Be(first with { Replayed = true });
        revoke.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [SkippableFact]
    public async Task ADM_05_A_workspace_cap_set_by_staff_is_the_one_its_members_see()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        await SkipUnlessAdminKeyAsync();
        var (token, workspaceRef) = await fixture.CreateIsolatedWorkspaceAsync();
        var request = new AdminSetWorkspaceCreditCapRequest(Staff(), 300m);
        string url = ServicesFixture.ManagementUrl($"/api/internal/admin/workspaces/{workspaceRef}/credit-cap");

        using var first = await fixture.SendAdminAsync(HttpMethod.Put, url, request);
        Skip.If(first.StatusCode == HttpStatusCode.NotFound && await ServicesFixture.ReadErrorCodeAsync(first) is null,
            "The management host has no Internal:AdminApiKey — skipping.");
        var again = await ReadAsync(await fixture.SendAdminAsync(HttpMethod.Put, url, request));

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        again.Replayed.Should().BeTrue();
        (await fixture.Management.As(token).GetUsageAsync(workspaceRef)).Workspace.MonthlyCreditCap.Should().Be(300m);
    }

    [SkippableFact]
    public async Task ADM_06_Signing_someone_out_everywhere_ends_their_refresh_tokens()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        await SkipUnlessAdminKeyAsync();
        var user = await fixture.CreateUserAsync();
        var session = await fixture.LoginAsync(user.Email, user.Password);
        Guid userRef = await UserRefAsync(user);
        var request = new AdminUserActionRequest(Staff());

        var first = await ReadAsync(await fixture.SendAdminAsync(HttpMethod.Post, Admin($"/users/{userRef}/sessions/revoke"), request));
        var again = await ReadAsync(await fixture.SendAdminAsync(HttpMethod.Post, Admin($"/users/{userRef}/sessions/revoke"), request));
        using var refresh = await fixture.RefreshAsync(session.RefreshToken);

        again.Should().Be(first with { Replayed = true });
        refresh.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [SkippableFact]
    public async Task ADM_07_Sign_in_turned_off_refuses_a_login_until_it_is_turned_back_on()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        await SkipUnlessAdminKeyAsync();
        var user = await fixture.CreateUserAsync();
        Guid userRef = await UserRefAsync(user);

        using var off = await fixture.SendAdminAsync(HttpMethod.Put, Admin($"/users/{userRef}/sign-in"), new AdminSetUserSignInRequest(Staff(), false));
        using var refused = await fixture.LoginRawAsync(user.Email, user.Password);
        using var on = await fixture.SendAdminAsync(HttpMethod.Put, Admin($"/users/{userRef}/sign-in"), new AdminSetUserSignInRequest(Staff(), true));
        using var allowed = await fixture.LoginRawAsync(user.Email, user.Password);

        off.StatusCode.Should().Be(HttpStatusCode.OK);
        refused.IsSuccessStatusCode.Should().BeFalse();
        on.StatusCode.Should().Be(HttpStatusCode.OK);
        allowed.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [SkippableFact]
    public async Task ADM_08_A_refused_change_is_refused_again_for_the_same_key()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        await SkipUnlessAdminKeyAsync();
        var user = await fixture.CreateUserAsync();
        Guid userRef = await UserRefAsync(user);
        var request = new AdminUserActionRequest(Staff());

        using var first = await fixture.SendAdminAsync(HttpMethod.Post, Admin($"/users/{userRef}/lockout/clear"), request);
        using var again = await fixture.SendAdminAsync(HttpMethod.Post, Admin($"/users/{userRef}/lockout/clear"), request);
        using var reset = await fixture.SendAdminAsync(HttpMethod.Post, Admin($"/users/{userRef}/password-reset"), new AdminUserActionRequest(Staff()));

        (await ServicesFixture.ReadErrorCodeAsync(first)).Should().Be("USER_NOT_LOCKED");
        (await ServicesFixture.ReadErrorCodeAsync(again)).Should().Be("STAFF_ACTION_REFUSED_BEFORE");
        reset.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [SkippableFact]
    public async Task ADM_09_A_suspended_member_loses_the_tenant_until_restored()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        await SkipUnlessAdminKeyAsync();
        string adminToken = (await fixture.CreateUserAsync()).Token;
        Guid tenantRef = await fixture.GetTenantRefAsync(adminToken);
        var member = await fixture.CreateUserInTenantAsync(adminToken, tenantRef);
        Guid memberRef = (await fixture.FindTenantMemberRefAsync(adminToken, tenantRef, member.Email))!.Value;
        string url = Admin($"/tenants/{tenantRef}/members/{memberRef}/suspension");
        var suspend = new AdminSetMemberSuspendedRequest(Staff(), true);

        var first = await ReadAsync(await fixture.SendAdminAsync(HttpMethod.Put, url, suspend));
        var again = await ReadAsync(await fixture.SendAdminAsync(HttpMethod.Put, url, suspend));
        var tenantsWhileSuspended = await fixture.GetTenantsAsync(member.Token);
        using var restore = await fixture.SendAdminAsync(HttpMethod.Put, url, new AdminSetMemberSuspendedRequest(Staff(), false));

        again.Should().Be(first with { Replayed = true });
        tenantsWhileSuspended.Should().NotContain(t => t.RefId == tenantRef);
        restore.StatusCode.Should().Be(HttpStatusCode.OK);
        (await fixture.GetTenantsAsync(member.Token)).Should().Contain(t => t.RefId == tenantRef);
    }

    [SkippableFact]
    public async Task ADM_10_A_tenants_last_administrator_is_neither_suspended_nor_removed()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        await SkipUnlessAdminKeyAsync();
        var owner = await fixture.CreateUserAsync();
        Guid tenantRef = await fixture.GetTenantRefAsync(owner.Token);
        await fixture.CreateUserInTenantAsync(owner.Token, tenantRef);
        Guid ownerRef = await UserRefAsync(owner);

        using var suspend = await fixture.SendAdminAsync(HttpMethod.Put, Admin($"/tenants/{tenantRef}/members/{ownerRef}/suspension"),
            new AdminSetMemberSuspendedRequest(Staff(), true));
        using var remove = await fixture.SendAdminAsync(HttpMethod.Post, Admin($"/tenants/{tenantRef}/members/{ownerRef}/remove"),
            new AdminRemoveMemberRequest(Staff()));

        (await ServicesFixture.ReadErrorCodeAsync(suspend)).Should().Be("TENANT_LAST_ADMINISTRATOR");
        (await ServicesFixture.ReadErrorCodeAsync(remove)).Should().BeOneOf("TENANT_LAST_ADMINISTRATOR", "MEMBER_ONLY_TENANT");
    }

    [SkippableFact]
    public async Task ADM_11_A_removed_member_keeps_their_own_tenant_and_a_repeat_replays()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        await SkipUnlessAdminKeyAsync();
        var owner = await fixture.CreateUserAsync();
        Guid tenantRef = await fixture.GetTenantRefAsync(owner.Token);
        var member = await fixture.CreateUserAsync();
        Guid ownTenant = await fixture.GetTenantRefAsync(member.Token);
        string invitation = await fixture.InviteAsync(owner.Token, tenantRef, member.Email);
        using (var accepted = await fixture.AcceptInvitationAsync(member.Token, invitation))
        {
            accepted.IsSuccessStatusCode.Should().BeTrue();
        }

        Guid memberRef = (await fixture.FindTenantMemberRefAsync(owner.Token, tenantRef, member.Email))!.Value;
        var request = new AdminRemoveMemberRequest(Staff());
        string url = Admin($"/tenants/{tenantRef}/members/{memberRef}/remove");

        var first = await ReadAsync(await fixture.SendAdminAsync(HttpMethod.Post, url, request));
        var again = await ReadAsync(await fixture.SendAdminAsync(HttpMethod.Post, url, request));

        again.Should().Be(first with { Replayed = true });
        var tenants = await fixture.GetTenantsAsync(member.Token);
        tenants.Should().NotContain(t => t.RefId == tenantRef);
        tenants.Should().Contain(t => t.RefId == ownTenant);
    }

    [SkippableFact]
    public async Task ADM_12_A_wrong_admin_key_is_refused_and_a_user_token_opens_nothing()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        await SkipUnlessAdminKeyAsync();
        var user = await fixture.CreateUserAsync();
        Guid tenantRef = await fixture.GetTenantRefAsync(user.Token);

        using var wrongKey = await fixture.SendAdminAsync(HttpMethod.Get, Admin($"/tenants/{tenantRef}/limits"), adminKey: "not-the-key");
        using var userToken = await fixture.SendAsync(HttpMethod.Get, Admin($"/tenants/{tenantRef}/limits"), user.Token);

        wrongKey.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ServicesFixture.ReadErrorCodeAsync(wrongKey)).Should().Be("ADMIN_API_KEY_INVALID");
        userToken.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task<Guid> UserRefAsync(ServicesFixture.TestUser user)
    {
        Guid tenantRef = await fixture.GetTenantRefAsync(user.Token);
        return (await fixture.FindTenantMemberRefAsync(user.Token, tenantRef, user.Email))!.Value;
    }
}
