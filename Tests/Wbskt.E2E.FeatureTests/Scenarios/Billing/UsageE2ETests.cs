using System.Net;
using FluentAssertions;
using Wbskt.Client.Sdk;
using Wbskt.Client.Sdk.Models;
using Wbskt.E2E.FeatureTests.Fixtures;
using Wbskt.Auth.Models;
using Wbskt.Management.Models;
using Wbskt.Models.Billing;

namespace Wbskt.E2E.FeatureTests.Scenarios.Billing;

/// <summary>
/// The usage API (#630) and a workspace's credit cap (#634): what the plan allows against what has been
/// used, on the auth host for seats and on the management host for credits, clients and runs.
///
/// Requires the auth and management hosts running. Skips gracefully when they are down.
/// </summary>
[Collection(AuthCollection.Name)]
public sealed class UsageE2ETests(ServicesFixture fixture)
{
    [SkippableFact]
    public async Task BILL_01_A_new_tenant_is_on_free_with_one_workspace_and_one_member()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, _) = await fixture.RegisterAndLoginUserAsync();
        Guid tenantRef = await fixture.GetTenantRefAsync(token);

        var usage = await fixture.Auth.As(token).GetTenantUsageAsync(tenantRef);

        usage.Plan.Should().Be(PlanCode.Free);
        usage.Workspaces.Used.Should().Be(1);
        usage.Members.Used.Should().Be(1);
        usage.Limits.CreditsPerMonth.Should().Be(FreeCreditsPerMonth);
    }

    [SkippableFact]
    public async Task BILL_02_A_workspaces_usage_shows_its_plan_and_the_cap_its_owner_sets()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspaceRef) = await fixture.RegisterAndLoginUserAsync();

        WorkspaceCreditCapResponse cap = await fixture.Management.As(token).SetCreditCapAsync(workspaceRef, new WorkspaceCreditCapRequest(250m));
        WorkspaceUsageResponse usage = await fixture.Management.As(token).GetUsageAsync(workspaceRef);

        cap.Should().Be(new WorkspaceCreditCapResponse(250m, 0m, false));
        usage.Plan.Should().Be(PlanCode.Free);
        usage.Account.Credits.Should().Be(new CreditUsageAmount(0m, FreeCreditsPerMonth, 0m, null, 0m));
        usage.Account.Messages.Throttled.Should().BeFalse();
        usage.Workspace.MonthlyCreditCap.Should().Be(250m);
        usage.Workspace.CreditsReserved.Should().Be(0m);
        usage.Days.Should().BeEmpty();
        usage.PeriodEnd.Should().Be(usage.PeriodStart.AddMonths(1));
    }

    [SkippableFact]
    public async Task BILL_03_A_negative_cap_is_refused()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspaceRef) = await fixture.RegisterAndLoginUserAsync();

        var response = await fixture.Management.As(token).SetCreditCapRawAsync(workspaceRef, new WorkspaceCreditCapRequest(-1m));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ServicesFixture.ReadErrorCodeAsync(response)).Should().Be("WORKSPACE_CREDIT_CAP_INVALID");
    }

    [SkippableFact]
    public async Task BILL_04_A_clients_messages_are_metered_on_its_workspace()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspaceRef) = await fixture.CreateIsolatedWorkspaceAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspaceRef, autoApproval: true);
        var clientName = $"e2e-meter-{Guid.NewGuid():N}";
        var (clientRefId, secret) = await fixture.RegisterClientAsync(pin, clientName);
        await using var client = new WbsktClient(
            new ClientConfig(E2EConfig.ManagementBaseUrl, E2EConfig.SocketWsBaseUrl, clientName, null),
            new InMemoryClientStorage(clientRefId, secret));
        await client.StartAsync();

        for (int i = 0; i < 3; i++)
        {
            await client.SendAsync("meter.reading", new { i });
        }

        // The meter moves when the event logger writes its batch, a moment after the messages.
        WorkspaceUsageResponse usage = await fixture.Management.As(token).GetUsageAsync(workspaceRef);
        for (var deadline = DateTime.UtcNow.AddSeconds(30); usage.Workspace.Messages < 3 && DateTime.UtcNow < deadline;)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500));
            usage = await fixture.Management.As(token).GetUsageAsync(workspaceRef);
        }

        usage.Workspace.Messages.Should().BeGreaterThanOrEqualTo(3, "the SDK also announces its capabilities, which is a message too");
        usage.Account.Messages.Used.Should().BeGreaterThanOrEqualTo(3, "the account pools every workspace of the tenant");
    }

    [SkippableFact]
    public async Task BILL_05_Without_payment_credentials_nothing_is_on_offer_and_checkout_is_unavailable()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        // The suite runs with no Dodo credentials, which is also how a deployment turns payments off.
        var (token, _) = await fixture.RegisterAndLoginUserAsync();
        Guid tenantRef = await fixture.GetTenantRefAsync(token);

        TenantBillingResponse billing = await fixture.Auth.As(token).GetBillingAsync(tenantRef);
        var checkout = await fixture.Auth.As(token).StartCheckoutRawAsync(tenantRef, new BillingCheckoutRequest(PlanCode.Maker, BillingInterval.Monthly));
        var portal = await fixture.Auth.As(token).OpenBillingPortalRawAsync(tenantRef);
        var webhook = await fixture.SendRawBodyAsync(HttpMethod.Post, ServicesFixture.AuthUrl("/api/callbacks/billing/dodo"), "{}", "application/json");

        billing.Plan.Should().Be(PlanCode.Free);
        billing.Subscription.Should().BeNull();
        billing.Plans.Should().BeEmpty();
        billing.CreditPacks.Should().BeEmpty();
        checkout.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await ServicesFixture.ReadErrorCodeAsync(checkout)).Should().Be("BILLING_UNAVAILABLE");
        portal.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        webhook.StatusCode.Should().Be(HttpStatusCode.NotFound, "an unconfigured provider takes no webhooks");
    }

    private const decimal FreeCreditsPerMonth = 2_000m;
}
