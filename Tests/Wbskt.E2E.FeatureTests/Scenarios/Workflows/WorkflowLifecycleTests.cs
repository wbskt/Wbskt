using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Wbskt.E2E.FeatureTests.Fixtures;
using Wbskt.Management.Models.Workflow;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;

namespace Wbskt.E2E.FeatureTests.Scenarios.Workflows;

/// <summary>
/// Deleting a workflow, listing its versions, and a webhook sender's Idempotency-Key.
///
/// Requires the auth, management and engine hosts running. Skips gracefully when they are down.
/// </summary>
[Collection(E2ECollection.Name)]
public sealed class WorkflowLifecycleTests(ServicesFixture fixture)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed record VersionList(List<WorkflowVersionDto> Items);

    [SkippableFact]
    public async Task WF_VER_01_EveryVersionIsListed_NewestFirst()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspace) = await fixture.RegisterAndLoginUserAsync();
        var (refId, _) = await PublishWebhookWorkflowAsync(token, workspace);
        await PublishWebhookWorkflowAsync(token, workspace, refId);

        var response = await Send(HttpMethod.Get, workspace, $"workflows/{refId}/versions", token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var list = await response.Content.ReadFromJsonAsync<VersionList>(JsonOptions);
        list!.Items.Select(v => (v.Version, v.Status)).Should().Equal((2, "Published"), (1, "Superseded"));
    }

    [SkippableFact]
    public async Task WF_DEL_01_ADeletedWorkflow_IsGone_AndCannotBePublishedAgain()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspace) = await fixture.RegisterAndLoginUserAsync();
        var (refId, _) = await PublishWebhookWorkflowAsync(token, workspace);

        (await Send(HttpMethod.Delete, workspace, $"workflows/{refId}", token)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await Send(HttpMethod.Get, workspace, $"workflows/{refId}", token)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Send(HttpMethod.Get, workspace, $"workflows/{refId}/versions", token)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Send(HttpMethod.Delete, workspace, $"workflows/{refId}", token)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var listed = await Send(HttpMethod.Get, workspace, "workflows", token);
        (await listed.Content.ReadAsStringAsync()).Should().NotContain(refId.ToString());

        var republish = await PublishRawAsync(token, workspace, refId, $"hook-{Guid.NewGuid():N}");
        republish.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ServicesFixture.ReadErrorCodeAsync(republish)).Should().Be("WORKFLOW_DELETED");
    }

    [SkippableFact]
    public async Task WF_DEL_02_AnotherWorkspacesWorkflow_IsNotFound_AndSurvives()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (ownerToken, ownerWorkspace) = await fixture.RegisterAndLoginUserAsync();
        var (refId, _) = await PublishWebhookWorkflowAsync(ownerToken, ownerWorkspace);
        var (strangerToken, strangerWorkspace) = await fixture.RegisterAndLoginUserAsync();

        (await Send(HttpMethod.Delete, strangerWorkspace, $"workflows/{refId}", strangerToken)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Send(HttpMethod.Get, ownerWorkspace, $"workflows/{refId}", ownerToken)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [SkippableFact]
    public async Task WF_IDEM_01_AWebhookRetriedWithTheSameKey_StartsOneRun()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspace) = await fixture.RegisterAndLoginUserAsync();
        var (refId, path) = await PublishWebhookWorkflowAsync(token, workspace);

        // Wait for the trigger to be live, without a key: a keyed delivery that arrives before the
        // trigger exists is still a delivery, and its retries would be dropped.
        var live = await ServicesFixture.PollAsync(async () =>
        {
            await fixture.SendWebhookAsync(workspace, path);
            return (await fixture.ListRunsAsync(token, workspace, refId)).Count > 0;
        }, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(1));
        live.Should().BeTrue("the webhook trigger should go live after publishing");
        await Task.Delay(TimeSpan.FromSeconds(2));
        int baseline = (await fixture.ListRunsAsync(token, workspace, refId)).Count;

        string key = $"delivery-{Guid.NewGuid():N}";
        for (var i = 0; i < 3; i++)
        {
            (await SendKeyedWebhookAsync(workspace, path, key)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        }

        var started = await ServicesFixture.PollAsync(
            async () => (await fixture.ListRunsAsync(token, workspace, refId)).Count == baseline + 1,
            TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(500));
        started.Should().BeTrue();
        await Task.Delay(TimeSpan.FromSeconds(2));
        (await fixture.ListRunsAsync(token, workspace, refId)).Count.Should().Be(baseline + 1);

        (await SendKeyedWebhookAsync(workspace, path, $"delivery-{Guid.NewGuid():N}")).StatusCode.Should().Be(HttpStatusCode.Accepted);
        var another = await ServicesFixture.PollAsync(
            async () => (await fixture.ListRunsAsync(token, workspace, refId)).Count == baseline + 2,
            TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(500));
        another.Should().BeTrue("a different key is a different delivery");
    }

    [SkippableFact]
    public async Task WF_IDEM_02_AMalformedKey_Is400()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var response = await SendKeyedWebhookAsync(Guid.NewGuid(), "anything", new string('k', 256));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── helpers ───────────────────────────────────────────────────────────────────────────

    private async Task<(Guid RefId, string Path)> PublishWebhookWorkflowAsync(string token, Guid workspace, Guid? refId = null)
    {
        var workflowRef = refId ?? Guid.NewGuid();
        var path = $"hook-{Guid.NewGuid():N}";
        var response = await PublishRawAsync(token, workspace, workflowRef, path);
        response.EnsureSuccessStatusCode();
        return (workflowRef, path);
    }

    private Task<HttpResponseMessage> PublishRawAsync(string token, Guid workspace, Guid refId, string path)
    {
        WorkflowDefinition definition = new WorkflowBuilder($"E2E-Lifecycle-{refId:N}", refId)
            .AddWebhookTrigger(path, "POST", WorkflowConcurrencyPolicy.AllowParallel, out _)
            .AddDelay(TimeSpan.FromMilliseconds(10))
            .BuildAndValidate();
        return Send(HttpMethod.Post, workspace, "workflows", token, new WorkflowPublishRequest(refId, $"E2E-Lifecycle-{refId:N}", null, definition));
    }

    private Task<HttpResponseMessage> Send(HttpMethod method, Guid workspace, string path, string token, object? body = null) =>
        fixture.SendAsync(method, ServicesFixture.ManagementUrl($"/api/workspaces/{workspace}/{path}"), token, body);

    private static async Task<HttpResponseMessage> SendKeyedWebhookAsync(Guid workspace, string path, string key)
    {
        using var http = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, ServicesFixture.ManagementUrl($"/api/callbacks/webhook/{workspace}/{Uri.EscapeDataString(path)}"))
        {
            Content = JsonContent.Create(new { test = "payload" })
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        return await http.SendAsync(request);
    }
}
