using System.Net;
using System.Net.Http.Json;
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
[Collection(EngineCollection.Name)]
public sealed class WorkflowLifecycleTests(ServicesFixture fixture)
{
    [SkippableFact]
    public async Task WF_VER_01_EveryVersionIsListed_NewestFirst()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspace) = await fixture.RegisterAndLoginUserAsync();
        var (refId, _) = await PublishWebhookWorkflowAsync(token, workspace);
        await PublishWebhookWorkflowAsync(token, workspace, refId);

        var versions = await fixture.Management.As(token).GetWorkflowVersionsAsync(workspace, refId);

        versions.Items.Select(v => (v.Version, v.Status)).Should().Equal((2, "Published"), (1, "Superseded"));
    }

    [SkippableFact]
    public async Task WF_DEL_01_ADeletedWorkflow_IsGone_AndCannotBePublishedAgain()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspace) = await fixture.RegisterAndLoginUserAsync();
        var (refId, _) = await PublishWebhookWorkflowAsync(token, workspace);

        var management = fixture.Management.As(token);
        (await management.DeleteWorkflowRawAsync(workspace, refId)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await management.GetWorkflowRawAsync(workspace, refId)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await management.GetWorkflowVersionsRawAsync(workspace, refId)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await management.DeleteWorkflowRawAsync(workspace, refId)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await management.GetWorkflowsAsync(workspace)).Items.Select(w => w.RefId).Should().NotContain(refId);

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

        (await fixture.Management.As(strangerToken).DeleteWorkflowRawAsync(strangerWorkspace, refId)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await fixture.Management.As(ownerToken).GetWorkflowRawAsync(ownerWorkspace, refId)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [SkippableFact]
    public async Task WF_IDEM_01_AWebhookRetriedWithTheSameKey_StartsOneRun()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspace) = await fixture.RegisterAndLoginUserAsync();
        var (refId, path) = await PublishWebhookWorkflowAsync(token, workspace);

        // Wait for the trigger to be live, without a key: a keyed delivery that arrives before the
        // trigger exists is still a delivery, and its retries would be dropped.
        await fixture.FireWebhookUntilRunAsync(token, workspace, refId, path, TimeSpan.FromSeconds(30));

        // Warm-up deliveries still on the queue can start runs after this point, so runs are counted
        // by the key each delivery carries in its body (and so in the run's trigger data), not by
        // the workflow's total.
        string key = $"delivery-{Guid.NewGuid():N}";
        for (var i = 0; i < 3; i++)
        {
            (await SendKeyedWebhookAsync(workspace, path, key)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        }

        await fixture.WaitForRunsCarryingAsync(token, workspace, refId, key, 1, TimeSpan.FromSeconds(30));

        // A different key is a different delivery. It is queued after the three retries, so its run
        // appearing is the signal that they have had their turn, instead of a wait of so many seconds.
        string otherKey = $"delivery-{Guid.NewGuid():N}";
        (await SendKeyedWebhookAsync(workspace, path, otherKey)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        await fixture.WaitForRunsCarryingAsync(token, workspace, refId, otherKey, 1, TimeSpan.FromSeconds(30));

        (await fixture.ListRunsCarryingAsync(token, workspace, refId, key)).Should().HaveCount(1, "the retries of one delivery start one run");
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
        return fixture.Management.As(token).PublishWorkflowRawAsync(workspace, new WorkflowPublishRequest(refId, $"E2E-Lifecycle-{refId:N}", null, definition));
    }

    private static async Task<HttpResponseMessage> SendKeyedWebhookAsync(Guid workspace, string path, string key)
    {
        // Bespoke: the Idempotency-Key header is what is under test, and the typed client sends none.
        using var http = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, ServicesFixture.ManagementUrl($"/api/callbacks/webhook/{workspace}/{Uri.EscapeDataString(path)}"))
        {
            // The key goes in the body too, so the run a delivery started can be told apart.
            Content = JsonContent.Create(new { delivery = key })
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        return await http.SendAsync(request);
    }
}
