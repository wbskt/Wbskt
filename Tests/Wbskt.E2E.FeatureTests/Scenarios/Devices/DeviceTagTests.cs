using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Wbskt.E2E.FeatureTests.Fixtures;

namespace Wbskt.E2E.FeatureTests.Scenarios.Devices;

/// <summary>
/// Device tags: setting, replacing and clearing them, what a tag may be, the client list's tag
/// filter, and the workspace's tag list. Tags are trimmed and lower-cased on the way in, so every
/// scenario that sets one in mixed case reads it back in lower case.
///
/// Requires the auth and management hosts running. Skips gracefully when they are down.
/// </summary>
[Collection(E2ECollection.Name)]
public sealed class DeviceTagTests(ServicesFixture fixture)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed record TagsDto(Guid ClientRefId, List<string> Tags);

    private sealed record ClientDto(Guid ClientRefId, string Name, List<string> Tags);

    private sealed record TagCountDto(string Tag, int ClientCount);

    private sealed record ListDto<T>(List<T> Items);

    // ── Setting tags ──────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task DEV_TAG_01_SettingTags_StoresThemTrimmedLowerCasedDistinctAndSorted()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspace) = await fixture.RegisterAndLoginUserAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspace);
        var (clientRef, _) = await fixture.RegisterClientAsync(pin, "tagged");

        var response = await SetTagsAsync(workspace, clientRef, token, ["Greenhouse", " garage ", "GARAGE"]);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = (await response.Content.ReadFromJsonAsync<TagsDto>(JsonOptions))!;
        body.ClientRefId.Should().Be(clientRef);
        body.Tags.Should().Equal("garage", "greenhouse");
        (await TagsOfAsync(workspace, clientRef, token)).Should().Equal("garage", "greenhouse");
        (await ListAsync(workspace, token, "")).Single(c => c.ClientRefId == clientRef).Tags.Should().Equal("garage", "greenhouse");
    }

    [SkippableFact]
    public async Task DEV_TAG_02_SettingTagsAgain_ReplacesThem_AndAnEmptyListClearsThem()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspace) = await fixture.RegisterAndLoginUserAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspace);
        var (clientRef, _) = await fixture.RegisterClientAsync(pin, "retagged");
        (await SetTagsAsync(workspace, clientRef, token, ["garage", "greenhouse"])).EnsureSuccessStatusCode();

        (await SetTagsAsync(workspace, clientRef, token, ["greenhouse", "shed"])).StatusCode.Should().Be(HttpStatusCode.OK);
        (await TagsOfAsync(workspace, clientRef, token)).Should().Equal("greenhouse", "shed");

        (await SetTagsAsync(workspace, clientRef, token, [])).StatusCode.Should().Be(HttpStatusCode.OK);
        (await TagsOfAsync(workspace, clientRef, token)).Should().BeEmpty();
    }

    [SkippableFact]
    public async Task DEV_TAG_03_AnInvalidOrMissingTag_IsRefused_AndTheTagsStayAsTheyWere()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspace) = await fixture.RegisterAndLoginUserAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspace);
        var (clientRef, _) = await fixture.RegisterClientAsync(pin, "kept");
        (await SetTagsAsync(workspace, clientRef, token, ["garage"])).EnsureSuccessStatusCode();

        foreach (var bad in new[] { "a,b", "", "-garage", new string('x', 33) })
        {
            var response = await SetTagsAsync(workspace, clientRef, token, ["shed", bad]);
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest, $"'{bad}' is not a valid tag");
            (await ServicesFixture.ReadErrorCodeAsync(response)).Should().Be("CLIENT_TAG_INVALID");
        }

        var missing = await Send(HttpMethod.Put, workspace, $"clients/{clientRef}/tags", token, new { });
        missing.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ServicesFixture.ReadErrorCodeAsync(missing)).Should().Be("CLIENT_TAG_INVALID");

        (await TagsOfAsync(workspace, clientRef, token)).Should().Equal("garage");
    }

    [SkippableFact]
    public async Task DEV_TAG_04_MoreThanTenDistinctTags_IsRefused_ButRepeatsDoNotCount()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspace) = await fixture.RegisterAndLoginUserAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspace);
        var (clientRef, _) = await fixture.RegisterClientAsync(pin, "many-tags");
        var ten = Enumerable.Range(0, 10).Select(i => $"t{i}").ToArray();

        var tooMany = await SetTagsAsync(workspace, clientRef, token, [.. ten, "t10"]);
        tooMany.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ServicesFixture.ReadErrorCodeAsync(tooMany)).Should().Be("CLIENT_TAGS_TOO_MANY");
        (await TagsOfAsync(workspace, clientRef, token)).Should().BeEmpty();

        (await SetTagsAsync(workspace, clientRef, token, [.. ten, "T0", " t1"])).StatusCode.Should().Be(HttpStatusCode.OK);
        (await TagsOfAsync(workspace, clientRef, token)).Should().HaveCount(10);
    }

    [SkippableFact]
    public async Task DEV_TAG_05_TaggingAnUnknownDevice_OrAnotherWorkspaces_Is404()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (ownerToken, ownerWorkspace) = await fixture.RegisterAndLoginUserAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(ownerToken, ownerWorkspace);
        var (clientRef, _) = await fixture.RegisterClientAsync(pin, "not-yours");
        (await SetTagsAsync(ownerWorkspace, clientRef, ownerToken, ["garage"])).EnsureSuccessStatusCode();
        var (strangerToken, strangerWorkspace) = await fixture.RegisterAndLoginUserAsync();

        var unknown = await SetTagsAsync(strangerWorkspace, Guid.NewGuid(), strangerToken, ["shed"]);
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ServicesFixture.ReadErrorCodeAsync(unknown)).Should().Be("CLIENT_NOT_FOUND");

        var foreign = await SetTagsAsync(strangerWorkspace, clientRef, strangerToken, ["shed"]);
        foreign.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ServicesFixture.ReadErrorCodeAsync(foreign)).Should().Be("CLIENT_NOT_FOUND");

        (await TagsOfAsync(ownerWorkspace, clientRef, ownerToken)).Should().Equal("garage");
    }

    // ── Filtering the list ────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task DEV_TAG_06_FilteringByTag_ListsOnlyDevicesCarryingIt_InTheWorkspaceAndPolicyLists()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspace) = await fixture.RegisterAndLoginUserAsync();
        var (policyRef, pin) = await fixture.CreatePolicyAsync(token, workspace);
        var (_, otherPin) = await fixture.CreatePolicyAsync(token, workspace);
        var (garage, _) = await fixture.RegisterClientAsync(pin, "garage-door");
        var (both, _) = await fixture.RegisterClientAsync(otherPin, "garage-greenhouse");
        await fixture.RegisterClientAsync(pin, "untagged");
        (await SetTagsAsync(workspace, garage, token, ["garage"])).EnsureSuccessStatusCode();
        (await SetTagsAsync(workspace, both, token, ["garage", "greenhouse"])).EnsureSuccessStatusCode();

        // The filter is case-insensitive, like the tags themselves.
        (await ListAsync(workspace, token, "?tag=Garage")).Select(c => c.ClientRefId).Should().BeEquivalentTo([garage, both]);
        (await fixture.GetPageAsync(ClientsUrl(workspace, "?tag=garage"), token)).TotalCount.Should().Be(2);
        (await ListAsync(workspace, token, "?tag=greenhouse")).Select(c => c.ClientRefId).Should().Equal(both);
        (await ListAsync(workspace, token, "?tag=attic")).Should().BeEmpty();
        (await ListAsync(workspace, token, "")).Should().HaveCount(3);

        var inPolicy = await ListAsync(workspace, token, "", $"clients/policy/{policyRef}?tag=garage");
        inPolicy.Select(c => c.ClientRefId).Should().Equal(garage);
    }

    [SkippableFact]
    public async Task DEV_TAG_07_AnInvalidTagFilter_Is400()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspace) = await fixture.RegisterAndLoginUserAsync();
        var (policyRef, _) = await fixture.CreatePolicyAsync(token, workspace);

        foreach (var path in new[] { "clients?tag=a,b", $"clients/policy/{policyRef}?tag=a,b" })
        {
            var response = await Send(HttpMethod.Get, workspace, path, token);
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest, path);
            (await ServicesFixture.ReadErrorCodeAsync(response)).Should().Be("CLIENT_TAG_INVALID");
        }
    }

    // ── The workspace's tags ──────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task DEV_TAG_08_TheWorkspaceTagList_CountsItsOwnDevicesOnly_AndDropsADeletedDevicesTags()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspace) = await fixture.RegisterAndLoginUserAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspace);
        var (first, _) = await fixture.RegisterClientAsync(pin, "first");
        var (second, _) = await fixture.RegisterClientAsync(pin, "second");
        (await SetTagsAsync(workspace, first, token, ["garage", "greenhouse"])).EnsureSuccessStatusCode();
        (await SetTagsAsync(workspace, second, token, ["garage"])).EnsureSuccessStatusCode();

        var (strangerToken, strangerWorkspace) = await fixture.RegisterAndLoginUserAsync();
        var (_, strangerPin) = await fixture.CreatePolicyAsync(strangerToken, strangerWorkspace);
        var (foreign, _) = await fixture.RegisterClientAsync(strangerPin, "foreign");
        (await SetTagsAsync(strangerWorkspace, foreign, strangerToken, ["garage", "attic"])).EnsureSuccessStatusCode();

        (await WorkspaceTagsAsync(workspace, token)).Should().Equal(new TagCountDto("garage", 2), new TagCountDto("greenhouse", 1));

        (await Send(HttpMethod.Delete, workspace, $"clients/{first}", token)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await WorkspaceTagsAsync(workspace, token)).Should().Equal(new TagCountDto("garage", 1));
        (await WorkspaceTagsAsync(strangerWorkspace, strangerToken)).Should().Equal(new TagCountDto("attic", 1), new TagCountDto("garage", 1));
    }

    // ── helpers ───────────────────────────────────────────────────────────────────────────

    private Task<HttpResponseMessage> SetTagsAsync(Guid workspace, Guid clientRef, string token, string[] tags) =>
        Send(HttpMethod.Put, workspace, $"clients/{clientRef}/tags", token, new { Tags = tags });

    private async Task<List<string>> TagsOfAsync(Guid workspace, Guid clientRef, string token)
    {
        var response = await Send(HttpMethod.Get, workspace, $"clients/{clientRef}", token);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<ClientDto>(JsonOptions))!.Tags;
    }

    private async Task<List<ClientDto>> ListAsync(Guid workspace, string token, string query, string? path = null)
    {
        var response = await Send(HttpMethod.Get, workspace, path ?? $"clients{query}", token);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<ListDto<ClientDto>>(JsonOptions))!.Items;
    }

    private async Task<List<TagCountDto>> WorkspaceTagsAsync(Guid workspace, string token)
    {
        var response = await Send(HttpMethod.Get, workspace, "clients/tags", token);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<ListDto<TagCountDto>>(JsonOptions))!.Items;
    }

    private static string ClientsUrl(Guid workspace, string query) =>
        ServicesFixture.ManagementUrl($"/api/workspaces/{workspace}/clients{query}");

    private Task<HttpResponseMessage> Send(HttpMethod method, Guid workspace, string path, string token, object? body = null) =>
        fixture.SendAsync(method, ServicesFixture.ManagementUrl($"/api/workspaces/{workspace}/{path}"), token, body);
}
