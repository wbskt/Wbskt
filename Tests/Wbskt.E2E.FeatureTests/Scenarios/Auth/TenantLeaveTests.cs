using System.Net;
using FluentAssertions;
using Wbskt.Auth.Models;
using Wbskt.E2E.FeatureTests.Fixtures;

namespace Wbskt.E2E.FeatureTests.Scenarios.Auth;

/// <summary>
/// Section 6d of Docs/Auth.Host.E2E.Scenarios.md — leaving a tenant
/// (<c>POST /api/v1/tenants/{tenantRef}/leave</c>, issue #150). The same removal deleting an account
/// makes from each shared tenant, for one tenant, and refused wherever it would strand someone.
///
/// Requires the auth host running. Skips gracefully when it is down.
/// </summary>
[Collection(AuthCollection.Name)]
public sealed class TenantLeaveTests(ServicesFixture fixture)
{
    [SkippableFact]
    public async Task AUTH_LEAVE_01_AnInvitedMember_Leaves()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var admin = await fixture.CreateUserAsync();
        var tenantRef = await fixture.GetTenantRefAsync(admin.Token);
        var member = await fixture.CreateUserInTenantAsync(admin.Token, tenantRef);

        (await fixture.Auth.As(member.Token).LeaveTenantRawAsync(tenantRef)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await fixture.GetTenantsAsync(member.Token)).Select(t => t.RefId).Should().NotContain(tenantRef);
        var members = await fixture.Auth.As(admin.Token).GetTenantMembersAsync(tenantRef, limit: 200);
        members.Items.Should().ContainSingle().Which.Email.Should().BeEquivalentTo(admin.Email);
    }

    [SkippableFact]
    public async Task AUTH_LEAVE_02_YourOnlyTenant_IsRefused()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var user = await fixture.CreateUserAsync();
        var tenantRef = await fixture.GetTenantRefAsync(user.Token);

        var response = await fixture.Auth.As(user.Token).LeaveTenantRawAsync(tenantRef);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ServicesFixture.ReadErrorCodeAsync(response)).Should().Be("TENANT_LEAVE_LAST");
    }

    [SkippableFact]
    public async Task AUTH_LEAVE_03_ATenantNobodyElseBelongsTo_IsRefused()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var admin = await fixture.CreateUserAsync();
        var sharedTenant = await fixture.GetTenantRefAsync(admin.Token);
        var member = await fixture.CreateUserInTenantAsync(admin.Token, sharedTenant);
        var ownTenant = (await fixture.GetTenantsAsync(member.Token)).Select(t => t.RefId).Single(r => r != sharedTenant);

        var response = await fixture.Auth.As(member.Token).LeaveTenantRawAsync(ownTenant);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ServicesFixture.ReadErrorCodeAsync(response)).Should().Be("TENANT_LEAVE_SOLE_MEMBER");
    }

    [SkippableFact]
    public async Task AUTH_LEAVE_04_TheOnlyAdministrator_IsRefused()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var admin = await fixture.CreateUserAsync();
        var tenantRef = await fixture.GetTenantRefAsync(admin.Token);
        await fixture.CreateUserInTenantAsync(admin.Token, tenantRef);
        await fixture.Auth.As(admin.Token).CreateTenantAsync(new CreateTenantRequest(Name: $"e2e-tenant-{Guid.NewGuid():N}", Description: null));

        var response = await fixture.Auth.As(admin.Token).LeaveTenantRawAsync(tenantRef);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ServicesFixture.ReadErrorCodeAsync(response)).Should().Be("TENANT_SOLE_ADMINISTRATOR");
    }

    [SkippableFact]
    public async Task AUTH_LEAVE_05_ATenantYouAreNotIn_ReadsAsNotFound()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var user = await fixture.CreateUserAsync();
        var stranger = await fixture.CreateUserAsync();

        var response = await fixture.Auth.As(user.Token).LeaveTenantRawAsync(await fixture.GetTenantRefAsync(stranger.Token));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ServicesFixture.ReadErrorCodeAsync(response)).Should().Be("TENANT_NOT_FOUND");
    }
}
