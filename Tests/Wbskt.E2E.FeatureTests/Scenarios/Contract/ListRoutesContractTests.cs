using System.Net;
using System.Text.Json;
using FluentAssertions;
using Wbskt.Api.Client;
using Wbskt.Auth.Models;
using Wbskt.E2E.FeatureTests.Fixtures;
using Wbskt.E2E.FeatureTests.Scenarios.Integrations;
using Wbskt.Management.Models;

namespace Wbskt.E2E.FeatureTests.Scenarios.Contract;

/// <summary>
/// The wire contract of every list route on the auth and management hosts, one theory row each.
/// Each row arranges just enough rows for its list, reads it as raw JSON and checks the shape the
/// route promises:
/// <list type="bullet">
/// <item><see cref="Paging.Offset"/>: a page (<c>items</c>, <c>nextCursor</c>) that also carries
/// <c>totalCount</c>, repeated in the <c>X-Total-Count</c> header.</item>
/// <item><see cref="Paging.Keyset"/>: a page with no count, in the body or the header.</item>
/// <item><see cref="Paging.Whole"/> and <see cref="Paging.WholeCounted"/>: a page that always holds
/// the whole list (<c>nextCursor</c> null, <c>limit</c> ignored), with <c>totalCount</c> only on the
/// counted one.</item>
/// <item><see cref="Paging.Array"/>: a bare JSON array, from the routes that predate the page shape.</item>
/// </list>
/// Where a row arranges two or more rows on a paged route, <c>limit=1</c> must give one item and a
/// cursor, and that cursor must lead to a different item.
///
/// <para>
/// Not covered: the readings series (<c>clients/{clientRef}/readings</c> answers buckets, not a
/// list), the CSV exports, the event-log summary, the deprecated <c>clients/policy/{policyRef}</c>
/// alias, and the internal routes, which callers outside the backend never see.
/// </para>
///
/// Requires the auth, management and engine hosts running (the run lists need runs). Skips
/// gracefully when they are down.
/// </summary>
[Collection(EngineCollection.Name)]
public sealed class ListRoutesContractTests(ServicesFixture fixture)
{
    /// <summary>How a list route pages, which is what its theory row expects to see.</summary>
    public enum Paging
    {
        /// <summary><c>items</c>, <c>nextCursor</c> and <c>totalCount</c>, plus <c>X-Total-Count</c>.</summary>
        Offset,

        /// <summary><c>items</c> and <c>nextCursor</c>; no count.</summary>
        Keyset,

        /// <summary>The page shape around the whole list: <c>nextCursor</c> always null, no count.</summary>
        Whole,

        /// <summary>As <see cref="Whole"/>, with <c>totalCount</c> in the body.</summary>
        WholeCounted,

        /// <summary>A bare JSON array.</summary>
        Array
    }

    private const string TotalCountHeader = "X-Total-Count";

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private static readonly TimeSpan ArrangeTimeout = TimeSpan.FromSeconds(30);

    private enum ApiHost
    {
        Auth,
        Management
    }

    /// <summary>The list to read, as whom, and how many rows the arrangement guarantees it holds.</summary>
    private sealed record Target(ApiHost Host, string Token, string Path, int Rows);

    private sealed record ListRoute(string Name, Paging Paging, Func<ServicesFixture, Task<Target>> Arrange);

    private sealed record Reading(HttpStatusCode Status, string Text, string? TotalCountHeader);

    // ── The routes ────────────────────────────────────────────────────────────────────────────────

    private static readonly ListRoute[] Routes =
    [
        // Management host, under /api/workspaces/{workspaceRef}.
        new("management registration-policies", Paging.Offset, async f =>
        {
            var ws = await f.CreateIsolatedWorkspaceAsync();
            await f.CreatePolicyAsync(ws.Token, ws.WorkspaceRef);
            await f.CreatePolicyAsync(ws.Token, ws.WorkspaceRef);
            return Workspace(ws, "registration-policies", rows: 2);
        }),
        new("management clients", Paging.Offset, async f =>
        {
            var ws = await f.CreateIsolatedWorkspaceAsync();
            var (_, pin) = await f.CreatePolicyAsync(ws.Token, ws.WorkspaceRef);
            await f.RegisterClientAsync(pin, "contract-a");
            await f.RegisterClientAsync(pin, "contract-b");
            return Workspace(ws, "clients", rows: 2);
        }),
        new("management clients/tags", Paging.Whole, async f =>
        {
            var ws = await f.CreateIsolatedWorkspaceAsync();
            var client = await RegisterOneClientAsync(f, ws);
            await f.Management.As(ws.Token).SetClientTagsAsync(ws.WorkspaceRef, client, new SetClientTagsRequest(["contract-a", "contract-b"]));
            return Workspace(ws, "clients/tags", rows: 2);
        }),
        new("management clients/{clientRef}/state", Paging.Whole, async f =>
        {
            var ws = await f.CreateIsolatedWorkspaceAsync();
            var client = await RegisterOneClientAsync(f, ws);
            return Workspace(ws, $"clients/{client}/state", rows: 0);
        }),
        new("management clients/{clientRef}/comms", Paging.Keyset, async f =>
        {
            // A device that never connected has said nothing, so this checks the shape only.
            var ws = await f.CreateIsolatedWorkspaceAsync();
            var client = await RegisterOneClientAsync(f, ws);
            return Workspace(ws, $"clients/{client}/comms", rows: 0);
        }),
        new("management message-templates", Paging.Offset, async f =>
        {
            var ws = await f.CreateIsolatedWorkspaceAsync();
            var management = f.Management.As(ws.Token);
            await management.CreateMessageTemplateAsync(ws.WorkspaceRef, new MessageTemplateRequest("contract-a", "contract.test", "{}", null));
            await management.CreateMessageTemplateAsync(ws.WorkspaceRef, new MessageTemplateRequest("contract-b", "contract.test", "{}", null));
            return Workspace(ws, "message-templates", rows: 2);
        }),
        new("management event-logs", Paging.Keyset, async f =>
        {
            // Two device registrations log two entries (through the bus, so the read polls for them).
            var ws = await f.CreateIsolatedWorkspaceAsync();
            var (_, pin) = await f.CreatePolicyAsync(ws.Token, ws.WorkspaceRef);
            await f.RegisterClientAsync(pin, "contract-a");
            await f.RegisterClientAsync(pin, "contract-b");
            return Workspace(ws, "event-logs", rows: 2);
        }),
        new("management integrations", Paging.WholeCounted, async f =>
        {
            var ws = await f.CreateIsolatedWorkspaceAsync();
            var integrations = new IntegrationsApi(f);
            await integrations.CreateAsync(ws.Token, ws.WorkspaceRef, IntegrationsApi.Telegram("Contract bot A"));
            await integrations.CreateAsync(ws.Token, ws.WorkspaceRef, IntegrationsApi.Telegram("Contract bot B"));
            return Workspace(ws, "integrations", rows: 2);
        }),
        new("management workflows", Paging.Offset, async f =>
        {
            var ws = await f.CreateIsolatedWorkspaceAsync();
            await PublishManualWorkflowAsync(f, ws, Guid.NewGuid());
            await PublishManualWorkflowAsync(f, ws, Guid.NewGuid());
            return Workspace(ws, "workflows", rows: 2);
        }),
        new("management workflows/{workflowRef}/versions", Paging.Whole, async f =>
        {
            var ws = await f.CreateIsolatedWorkspaceAsync();
            var refId = Guid.NewGuid();
            await PublishManualWorkflowAsync(f, ws, refId);
            await PublishManualWorkflowAsync(f, ws, refId);
            return Workspace(ws, $"workflows/{refId}/versions", rows: 2);
        }),
        new("management workflows/{workflowRef}/runs", Paging.Keyset, async f =>
        {
            var ws = await f.CreateIsolatedWorkspaceAsync();
            var (workflow, _) = await StartRunsAsync(f, ws, count: 2);
            return Workspace(ws, $"workflows/{workflow}/runs", rows: 2);
        }),
        new("management runs", Paging.Keyset, async f =>
        {
            var ws = await f.CreateIsolatedWorkspaceAsync();
            await StartRunsAsync(f, ws, count: 2);
            return Workspace(ws, "runs", rows: 2);
        }),
        new("management runs/{runRef}/history", Paging.Keyset, async f =>
        {
            // A finished run has recorded more than one event, and adds no more.
            var ws = await f.CreateIsolatedWorkspaceAsync();
            var (_, runs) = await StartRunsAsync(f, ws, count: 1);
            (await f.WaitForRunTerminalAsync(ws.Token, ws.WorkspaceRef, runs[0], ArrangeTimeout)).Should().NotBeNull("a trigger-only run finishes at once");
            return Workspace(ws, $"runs/{runs[0]}/history", rows: 2);
        }),

        // Auth host.
        new("auth workspaces", Paging.Array, async f =>
        {
            var ws = await f.CreateIsolatedWorkspaceAsync();
            return new Target(ApiHost.Auth, ws.Token, "/api/workspaces", Rows: 1);
        }),
        new("auth workspaces/{workspaceRef}/members", Paging.Offset, async f =>
        {
            var (admin, tenant) = await NewTenantAsync(f);
            var workspace = await f.CreateWorkspaceAsync(admin.Token);
            var member = await f.CreateUserInTenantAsync(admin.Token, tenant);
            using var added = await f.Auth.As(admin.Token).AddWorkspaceMemberRawAsync(workspace, new AddMemberRequest(member.Email));
            new[] { HttpStatusCode.OK, HttpStatusCode.Created, HttpStatusCode.NoContent, HttpStatusCode.Conflict }
                .Should().Contain(added.StatusCode, "the invited member joins the workspace, unless the invitation already put them there");
            return new Target(ApiHost.Auth, admin.Token, $"/api/workspaces/{workspace}/members", Rows: 2);
        }),
        new("auth auth/sessions", Paging.Array, async f =>
        {
            var user = await f.CreateUserAsync();
            return new Target(ApiHost.Auth, user.Token, "/api/auth/sessions", Rows: 1);
        }),
        new("auth tenants", Paging.Array, async f =>
        {
            var user = await f.CreateUserAsync();
            return new Target(ApiHost.Auth, user.Token, "/api/tenants", Rows: 1);
        }),
        new("auth tenants/{tenantRef}/invitations", Paging.Offset, async f =>
        {
            var (admin, tenant) = await NewTenantAsync(f);
            await f.InviteAsync(admin.Token, tenant, ServicesFixture.NewCredentials().Email);
            await f.InviteAsync(admin.Token, tenant, ServicesFixture.NewCredentials().Email);
            return Tenant(admin, tenant, "invitations", rows: 2);
        }),
        new("auth tenants/{tenantRef}/roles", Paging.Offset, async f =>
        {
            var (admin, tenant) = await NewTenantAsync(f);
            await f.CreateRoleAsync(admin.Token, tenant);
            await f.CreateRoleAsync(admin.Token, tenant);
            return Tenant(admin, tenant, "roles", rows: 2);
        }),
        new("auth tenants/{tenantRef}/roles/{roleRef}/permissions", Paging.Array, async f =>
        {
            var (admin, tenant) = await NewTenantAsync(f);
            var role = await f.CreateRoleAsync(admin.Token, tenant);
            return Tenant(admin, tenant, $"roles/{role}/permissions", rows: 0);
        }),
        new("auth tenants/{tenantRef}/groups", Paging.Offset, async f =>
        {
            var (admin, tenant) = await NewTenantAsync(f);
            await f.CreateGroupAsync(admin.Token, tenant);
            await f.CreateGroupAsync(admin.Token, tenant);
            return Tenant(admin, tenant, "groups", rows: 2);
        }),
        new("auth tenants/{tenantRef}/groups/{groupRef}/roles", Paging.Array, async f =>
        {
            var (admin, tenant) = await NewTenantAsync(f);
            var group = await f.CreateGroupAsync(admin.Token, tenant);
            return Tenant(admin, tenant, $"groups/{group}/roles", rows: 0);
        }),
        new("auth tenants/{tenantRef}/permissions", Paging.Offset, async f =>
        {
            // The catalogue is code-defined and holds far more than two permissions.
            var (admin, tenant) = await NewTenantAsync(f);
            return Tenant(admin, tenant, "permissions", rows: 2);
        }),
        new("auth tenants/{tenantRef}/members", Paging.Offset, async f =>
        {
            var (admin, tenant) = await NewTenantAsync(f);
            await f.CreateUserInTenantAsync(admin.Token, tenant);
            return Tenant(admin, tenant, "members", rows: 2);
        }),
        new("auth tenants/{tenantRef}/members/{userRef}/roles", Paging.Array, async f =>
        {
            var (admin, tenant, self) = await NewTenantWithSelfAsync(f);
            return Tenant(admin, tenant, $"members/{self}/roles", rows: 0);
        }),
        new("auth tenants/{tenantRef}/members/{userRef}/permissions", Paging.Array, async f =>
        {
            var (admin, tenant, self) = await NewTenantWithSelfAsync(f);
            return Tenant(admin, tenant, $"members/{self}/permissions", rows: 0);
        }),
        new("auth tenants/{tenantRef}/members/{userRef}/groups", Paging.Array, async f =>
        {
            var (admin, tenant, self) = await NewTenantWithSelfAsync(f);
            return Tenant(admin, tenant, $"members/{self}/groups", rows: 0);
        }),
    ];

    /// <summary>One row per list route: its name and the paging it is expected to answer with.</summary>
    public static TheoryData<string, Paging> ListRoutes
    {
        get
        {
            var data = new TheoryData<string, Paging>();
            foreach (var route in Routes)
            {
                data.Add(route.Name, route.Paging);
            }

            return data;
        }
    }

    [SkippableTheory]
    [MemberData(nameof(ListRoutes))]
    public async Task CONTRACT_LIST_01_EveryListRoute_AnswersItsPagingShape(string route, Paging paging)
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var target = await Routes.Single(r => r.Name == route).Arrange(fixture);

        // Some lists fill through the bus (the event log) or the engine (runs), so read until the
        // arranged rows show up, then check the shape of that read.
        var whole = await ServicesFixture.PollUntilAsync<Reading>(
            $"{route} to list {target.Rows} rows",
            async () =>
            {
                var reading = await ReadAsync(target, query: null);
                return (reading.Status == HttpStatusCode.OK && CountItems(reading, paging) >= target.Rows, reading, $"{(int)reading.Status} {reading.Text}");
            },
            ArrangeTimeout,
            TimeSpan.FromMilliseconds(500));

        AssertShape(whole, paging, route);

        if (target.Rows < 2)
        {
            return;
        }

        switch (paging)
        {
            case Paging.Offset or Paging.Keyset:
            {
                var first = await ReadAsync(target, "limit=1");
                AssertShape(first, paging, $"{route}?limit=1");
                var (items, next) = Page(first);
                items.Should().HaveCount(1, $"{route} pages, and limit=1 asks for one item");
                next.Should().NotBeNullOrEmpty($"{route} holds {target.Rows} rows, so a page of one has a next page");

                if (paging == Paging.Offset)
                {
                    TotalCount(first).Should().BeGreaterThanOrEqualTo(target.Rows, $"{route} counts the whole list, not the page");
                }

                var second = await ReadAsync(target, $"limit=1&cursor={Uri.EscapeDataString(next!)}");
                AssertShape(second, paging, $"{route}?cursor=");
                var (nextItems, _) = Page(second);
                nextItems.Should().HaveCount(1, $"{route}'s cursor leads to the next row");
                nextItems[0].GetRawText().Should().NotBe(items[0].GetRawText(), $"{route}'s next page starts after the first");
                break;
            }

            case Paging.Whole or Paging.WholeCounted:
            {
                // These routes take no paging parameters, so limit=1 changes nothing.
                var limited = await ReadAsync(target, "limit=1");
                AssertShape(limited, paging, $"{route}?limit=1");
                Page(limited).Items.Should().HaveCountGreaterThanOrEqualTo(target.Rows, $"{route} always answers with the whole list");
                break;
            }
        }
    }

    // ── Shape ─────────────────────────────────────────────────────────────────────────────────────

    private static void AssertShape(Reading reading, Paging paging, string because)
    {
        reading.Status.Should().Be(HttpStatusCode.OK, $"{because}: {reading.Text}");

        using var body = JsonDocument.Parse(reading.Text);
        var root = body.RootElement;

        if (paging == Paging.Array)
        {
            root.ValueKind.Should().Be(JsonValueKind.Array, $"{because} answers a bare array");
            reading.TotalCountHeader.Should().BeNull($"{because} is not offset-paged");
            return;
        }

        root.ValueKind.Should().Be(JsonValueKind.Object, $"{because} answers a page");
        root.TryGetProperty("items", out var items).Should().BeTrue($"{because}: a page carries items");
        items.ValueKind.Should().Be(JsonValueKind.Array, $"{because}: items is an array");

        // nextCursor is always written, as null on the last page, so a caller can test it without
        // first testing whether it is there.
        root.TryGetProperty("nextCursor", out var next).Should().BeTrue($"{because}: a page always carries nextCursor, null on the last page");
        (next.ValueKind is JsonValueKind.String or JsonValueKind.Null).Should().BeTrue($"{because}: nextCursor is an opaque string or null, not {next.ValueKind}");

        var hasTotal = root.TryGetProperty("totalCount", out var total);
        switch (paging)
        {
            case Paging.Offset:
                hasTotal.Should().BeTrue($"{because} is offset-paged and counts the whole list");
                total.ValueKind.Should().Be(JsonValueKind.Number, $"{because}: totalCount is a number");
                total.GetInt32().Should().BeGreaterThanOrEqualTo(items.GetArrayLength(), $"{because}: the whole list holds at least this page");
                reading.TotalCountHeader.Should().Be(total.GetInt32().ToString(System.Globalization.CultureInfo.InvariantCulture),
                    $"{because}: {TotalCountHeader} repeats totalCount for the callers that read the header");
                break;

            case Paging.Keyset:
                hasTotal.Should().BeFalse($"{because} pages by key and does not count the list");
                reading.TotalCountHeader.Should().BeNull($"{because} pages by key and does not count the list");
                break;

            case Paging.Whole:
                next.ValueKind.Should().Be(JsonValueKind.Null, $"{because} answers the whole list in one page");
                hasTotal.Should().BeFalse($"{because} does not count the list");
                reading.TotalCountHeader.Should().BeNull($"{because} is not offset-paged");
                break;

            case Paging.WholeCounted:
                next.ValueKind.Should().Be(JsonValueKind.Null, $"{because} answers the whole list in one page");
                hasTotal.Should().BeTrue($"{because} counts the list");
                total.GetInt32().Should().Be(items.GetArrayLength(), $"{because}: the one page is the whole list");
                reading.TotalCountHeader.Should().BeNull($"{because} is not offset-paged");
                break;
        }
    }

    private static int CountItems(Reading reading, Paging paging)
    {
        try
        {
            using var body = JsonDocument.Parse(reading.Text);
            var root = body.RootElement;
            if (paging == Paging.Array)
            {
                return root.ValueKind == JsonValueKind.Array ? root.GetArrayLength() : -1;
            }

            return root.ValueKind == JsonValueKind.Object && root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array
                ? items.GetArrayLength()
                : -1;
        }
        catch (JsonException)
        {
            return -1;
        }
    }

    private static (List<JsonElement> Items, string? NextCursor) Page(Reading reading)
    {
        using var body = JsonDocument.Parse(reading.Text);
        var root = body.RootElement;
        var items = root.GetProperty("items").EnumerateArray().Select(item => item.Clone()).ToList();
        var next = root.GetProperty("nextCursor");
        return (items, next.ValueKind == JsonValueKind.String ? next.GetString() : null);
    }

    private static int TotalCount(Reading reading)
    {
        using var body = JsonDocument.Parse(reading.Text);
        return body.RootElement.GetProperty("totalCount").GetInt32();
    }

    /// <summary>GETs the list through the host's typed client, untouched, with an optional query.</summary>
    private async Task<Reading> ReadAsync(Target target, string? query)
    {
        WbsktApiClient client = target.Host == ApiHost.Auth
            ? fixture.Auth.As(target.Token)
            : (WbsktApiClient)fixture.Management.As(target.Token);

        var path = query is null ? target.Path : $"{target.Path}{(target.Path.Contains('?') ? '&' : '?')}{query}";
        using var response = await client.SendRawAsync(HttpMethod.Get, path);
        var text = await response.Content.ReadAsStringAsync();
        var header = response.Headers.TryGetValues(TotalCountHeader, out var values) ? string.Join(",", values) : null;
        return new Reading(response.StatusCode, text, header);
    }

    // ── Arrangement ───────────────────────────────────────────────────────────────────────────────

    private static Target Workspace(ServicesFixture.WorkspaceHandle ws, string suffix, int rows) =>
        new(ApiHost.Management, ws.Token, $"/api/workspaces/{ws.WorkspaceRef}/{suffix}", rows);

    private static Target Tenant(ServicesFixture.TestUser admin, Guid tenant, string suffix, int rows) =>
        new(ApiHost.Auth, admin.Token, $"/api/tenants/{tenant}/{suffix}", rows);

    /// <summary>A brand-new user and the tenant they were given at sign-up, which they administer.</summary>
    private static async Task<(ServicesFixture.TestUser Admin, Guid Tenant)> NewTenantAsync(ServicesFixture f)
    {
        var admin = await f.CreateUserAsync();
        return (admin, await f.GetTenantRefAsync(admin.Token));
    }

    /// <summary><see cref="NewTenantAsync"/>, with the administrator's own member reference.</summary>
    private static async Task<(ServicesFixture.TestUser Admin, Guid Tenant, Guid Self)> NewTenantWithSelfAsync(ServicesFixture f)
    {
        var (admin, tenant) = await NewTenantAsync(f);
        var self = await f.FindTenantMemberRefAsync(admin.Token, tenant, admin.Email)
            ?? throw new InvalidOperationException($"{admin.Email} is not listed in the tenant they created.");
        return (admin, tenant, self);
    }

    private static async Task<Guid> RegisterOneClientAsync(ServicesFixture f, ServicesFixture.WorkspaceHandle ws)
    {
        var (_, pin) = await f.CreatePolicyAsync(ws.Token, ws.WorkspaceRef);
        return (await f.RegisterClientAsync(pin, "contract-device")).ClientRefId;
    }

    /// <summary>Publishes a workflow that is only a manual trigger under <paramref name="refId"/>; again under the same one is a new version.</summary>
    private static async Task<Guid> PublishManualWorkflowAsync(ServicesFixture f, ServicesFixture.WorkspaceHandle ws, Guid refId)
    {
        var name = $"E2E-Contract-{refId:N}";
        var definition = new WorkflowBuilder(name, refId)
            .AddManualTrigger(out var triggerNodeId)
            .BuildAndValidate();
        await f.PublishWorkflowAsync(ws.Token, ws.WorkspaceRef, refId, name, JsonSerializer.SerializeToElement(definition, JsonOpts));
        return triggerNodeId;
    }

    /// <summary>Publishes a manual workflow and starts <paramref name="count"/> runs of it.</summary>
    private static async Task<(Guid Workflow, List<Guid> Runs)> StartRunsAsync(ServicesFixture f, ServicesFixture.WorkspaceHandle ws, int count)
    {
        var workflow = Guid.NewGuid();
        var trigger = await PublishManualWorkflowAsync(f, ws, workflow);

        var runs = new List<Guid>();
        for (var i = 0; i < count; i++)
        {
            runs.Add((await f.StartManualRunAsync(ws.Token, ws.WorkspaceRef, workflow, trigger.ToString(), idempotencyKey: null)).RunRefId);
        }

        return (workflow, runs);
    }
}
