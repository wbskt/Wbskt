using System.Net;
using System.Text.Json;
using FluentAssertions;
using Wbskt.E2E.FeatureTests.Fixtures;

namespace Wbskt.E2E.FeatureTests.Scenarios.Devices;

/// <summary>
/// The client routes the API style guide replaced still answer for one release, so the console can
/// move over first: <c>PATCH {clientRef}/name</c> and <c>/status</c> (now <c>PATCH {clientRef}</c>),
/// <c>POST {clientRef}/command</c> (now <c>/commands</c>) and <c>GET policy/{policyRef}</c> (now
/// <c>GET ?policyRefId=</c>). Each one must behave exactly like the route that replaced it.
///
/// Requires the auth and management hosts running. Skips gracefully when they are down.
/// </summary>
[Collection(E2ECollection.Name)]
public sealed class LegacyClientRouteTests(ServicesFixture fixture)
{
    private const int Revoked = 2;

    [SkippableFact]
    public async Task LEGACY_01_OldFieldRoutes_ChangeTheSameFields()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspace) = await fixture.RegisterAndLoginUserAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspace);
        var (clientRef, _) = await fixture.RegisterClientAsync(pin, "legacy-old-name");

        (await Send(HttpMethod.Patch, workspace, $"clients/{clientRef}/name", token, new { Name = " legacy-renamed " }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Send(HttpMethod.Patch, workspace, $"clients/{clientRef}/name", token, new { Name = " " }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Send(HttpMethod.Patch, workspace, $"clients/{clientRef}/status", token, new { Status = Revoked }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var detail = await DetailAsync(workspace, clientRef, token);
        detail.GetProperty("name").GetString().Should().Be("legacy-renamed");
        detail.GetProperty("status").GetInt32().Should().Be(Revoked);
    }

    [SkippableFact]
    public async Task LEGACY_02_TheNewRoute_ChangesBothFieldsAtOnce()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspace) = await fixture.RegisterAndLoginUserAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspace);
        var (clientRef, _) = await fixture.RegisterClientAsync(pin, "both-fields");

        (await Send(HttpMethod.Patch, workspace, $"clients/{clientRef}", token, new { }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest, "a request that names no field changes nothing");
        (await Send(HttpMethod.Patch, workspace, $"clients/{clientRef}", token, new { Name = "both-renamed", Status = Revoked }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var detail = await DetailAsync(workspace, clientRef, token);
        detail.GetProperty("name").GetString().Should().Be("both-renamed");
        detail.GetProperty("status").GetInt32().Should().Be(Revoked);
    }

    [SkippableFact]
    public async Task LEGACY_03_TheOldCommandRoute_AnswersLikeTheNewOne()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspace) = await fixture.RegisterAndLoginUserAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspace);
        var (clientRef, _) = await fixture.RegisterClientAsync(pin, "legacy-offline");
        var command = new { type = "e2e.blink", payload = "{}" };

        var old = await Send(HttpMethod.Post, workspace, $"clients/{clientRef}/command", token, command);
        var current = await Send(HttpMethod.Post, workspace, $"clients/{clientRef}/commands", token, command);

        old.StatusCode.Should().Be(HttpStatusCode.Conflict, "the device has never connected");
        current.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ServicesFixture.ReadErrorCodeAsync(old)).Should().Be("DEVICE_OFFLINE");
        (await ServicesFixture.ReadErrorCodeAsync(current)).Should().Be("DEVICE_OFFLINE");
    }

    [SkippableFact]
    public async Task LEGACY_04_TheOldPolicyRoute_ListsTheSameClients()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspace) = await fixture.RegisterAndLoginUserAsync();
        var (policyRef, pin) = await fixture.CreatePolicyAsync(token, workspace);
        var (_, otherPin) = await fixture.CreatePolicyAsync(token, workspace);
        var mine = new[] { (await fixture.RegisterClientAsync(pin, "in-policy-1")).ClientRefId, (await fixture.RegisterClientAsync(pin, "in-policy-2")).ClientRefId };
        await fixture.RegisterClientAsync(otherPin, "other-policy");

        var old = await ClientRefsAsync(workspace, $"clients/policy/{policyRef}", token);
        var current = await ClientRefsAsync(workspace, $"clients?policyRefId={policyRef}", token);

        current.Should().BeEquivalentTo(mine, "the filter keeps only that policy's clients");
        old.Should().BeEquivalentTo(current);
    }

    private async Task<List<Guid>> ClientRefsAsync(Guid workspace, string path, string token)
    {
        var response = await Send(HttpMethod.Get, workspace, path, token);
        response.StatusCode.Should().Be(HttpStatusCode.OK, path);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("items").EnumerateArray().Select(c => c.GetProperty("clientRefId").GetGuid()).ToList();
    }

    private async Task<JsonElement> DetailAsync(Guid workspace, Guid clientRef, string token)
    {
        var response = await Send(HttpMethod.Get, workspace, $"clients/{clientRef}", token);
        response.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.Clone();
    }

    private Task<HttpResponseMessage> Send(HttpMethod method, Guid workspace, string path, string token, object? body = null) =>
        fixture.SendAsync(method, ServicesFixture.ManagementUrl($"/api/workspaces/{workspace}/{path}"), token, body);
}
