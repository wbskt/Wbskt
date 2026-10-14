using System.Net;
using System.Text.Json;
using FluentAssertions;
using Wbskt.E2E.FeatureTests.Fixtures;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;

namespace Wbskt.E2E.FeatureTests.Scenarios.Integrations;

/// <summary>
/// Integrations and the workflows that send through them: a publish names only this workspace's
/// integrations of the right kind, what is published reads as using them, and an integration a
/// published workflow uses cannot be deleted out from under it.
/// </summary>
[Collection(EngineCollection.Name)]
public sealed class IntegrationUsageE2ETests(ServicesFixture fixture)
{
    private readonly IntegrationsApi _api = new(fixture);

    [SkippableFact]
    public async Task INT_USE_01_PublishingWithAnUnknownIntegration_IsRefused()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        var (token, ws) = await _api.NewWorkspaceAsync();

        var definition = new WorkflowBuilder("E2E unknown integration", Guid.NewGuid())
            .AddManualTrigger()
            .AddTelegram(Guid.NewGuid(), "1", "hello")
            .BuildAndValidate();

        using var response = await _api.PublishRawAsync(token, ws, definition);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ServicesFixture.ReadErrorCodeAsync(response)).Should().Be("WORKFLOW_VALIDATION_FAILED");
        (await response.Content.ReadAsStringAsync()).Should().Contain("INTEGRATION_NOT_FOUND");
    }

    [SkippableFact]
    public async Task INT_USE_02_PublishingWithAnotherWorkspacesIntegration_IsRefused_AsNotFound()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        var (token, ws) = await _api.NewWorkspaceAsync();
        var otherWs = await fixture.CreateWorkspaceAsync(token);
        var foreign = IntegrationsApi.RefOf(await _api.CreateAsync(token, otherWs, IntegrationsApi.Telegram("Elsewhere")));

        var definition = new WorkflowBuilder("E2E foreign integration", Guid.NewGuid())
            .AddManualTrigger()
            .AddTelegram(foreign, "1", "hello")
            .BuildAndValidate();

        using var response = await _api.PublishRawAsync(token, ws, definition);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("INTEGRATION_NOT_FOUND");
    }

    [SkippableFact]
    public async Task INT_USE_03_PublishingWithTheWrongKindOfIntegration_IsRefused()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        var (token, ws) = await _api.NewWorkspaceAsync();
        var bot = IntegrationsApi.RefOf(await _api.CreateAsync(token, ws, IntegrationsApi.Telegram("Bot")));

        var definition = new WorkflowBuilder("E2E wrong kind", Guid.NewGuid())
            .AddManualTrigger()
            .AddEmail(bot, "a@wbskt.test", "s", "b")
            .BuildAndValidate();

        using var response = await _api.PublishRawAsync(token, ws, definition);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("INTEGRATION_WRONG_TYPE");
    }

    [SkippableFact]
    public async Task INT_USE_04_ValidatingADraft_ReportsEachIntegrationProblemOnItsStep()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        var (token, ws) = await _api.NewWorkspaceAsync();
        var bot = IntegrationsApi.RefOf(await _api.CreateAsync(token, ws, IntegrationsApi.Bearer("API", "http://127.0.0.1:9")));

        var definition = new WorkflowBuilder("E2E draft", Guid.NewGuid())
            .AddManualTrigger()
            .AddTelegram(bot, "1", "wrong kind")
            .AddDiscord(Guid.NewGuid(), "missing")
            .AddWebhook("POST", "http://127.0.0.2:9/elsewhere", null, out _, integrationRef: bot)
            .AddWebhook("POST", "/conflict", null, out _, integrationRef: bot, headers: new Dictionary<string, string> { ["authorization"] = "Bearer mine" })
            .Build();

        var result = await _api.ValidateAsync(token, ws, definition);

        result.IsValid.Should().BeFalse();
        result.Issues.Select(i => i.Code).Should().Contain(["INTEGRATION_WRONG_TYPE", "INTEGRATION_NOT_FOUND", "WEBHOOK_ORIGIN_MISMATCH"]);
        result.Issues.Should().OnlyContain(i => i.NodeId != null, "each problem names the step to fix");
        result.Issues.Select(i => i.Code).Should().Contain(c => c == "WEBHOOK_HEADER_CONFLICT" || c == "WEBHOOK_HEADER_FORBIDDEN",
            "a step may not set the header its integration's credential travels in");
    }

    [SkippableFact]
    public async Task INT_USE_05_AStepWithoutAnIntegration_IsInvalidEvenBeforePublishing()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        var (token, ws) = await _api.NewWorkspaceAsync();

        var definition = new WorkflowBuilder("E2E missing ref", Guid.NewGuid())
            .AddManualTrigger()
            .AddNtfy(Guid.Empty, "no integration", priority: 9)
            .AddWebhook("POST", "/relative-without-integration", null, out _)
            .Build();

        var result = await _api.ValidateAsync(token, ws, definition);

        result.IsValid.Should().BeFalse();
        result.Issues.Select(i => i.Code).Should().Contain(["INTEGRATION_REF_MISSING", "NTFY_PRIORITY_INVALID", "WEBHOOK_URL_RELATIVE"]);
    }

    [SkippableFact]
    public async Task INT_USE_06_Usage_ListsEachPublishedStep_AndDeleteIsRefusedWhileInUse()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        var (token, ws) = await _api.NewWorkspaceAsync();
        var bot = IntegrationsApi.RefOf(await _api.CreateAsync(token, ws, IntegrationsApi.Telegram("Bot")));

        var definition = new WorkflowBuilder("E2E usage", Guid.NewGuid())
            .AddManualTrigger()
            .AddTelegram(bot, "1", "first", name: "Tell the owner")
            .AddTelegram(bot, "2", "second", name: "Tell the neighbour")
            .BuildAndValidate();
        var workflow = await _api.PublishAsync(token, ws, definition);

        var usage = (await _api.UsageAsync(token, ws, bot)).GetProperty("usedBy").EnumerateArray().ToList();
        usage.Should().HaveCount(2);
        usage.Should().OnlyContain(u => u.GetProperty("workflowRefId").GetGuid() == workflow
                                        && u.GetProperty("workflowName").GetString() == "E2E usage"
                                        && u.GetProperty("status").GetString() == "published");
        usage.Select(u => u.GetProperty("stepName").GetString()).Should().BeEquivalentTo("Tell the owner", "Tell the neighbour");
        var stepIds = definition.Nodes.OfType<TelegramNotificationNode>().Select(n => n.NodeId);
        usage.Select(u => u.GetProperty("stepId").GetGuid()).Should().BeEquivalentTo(stepIds);

        var summary = (await _api.GetAsync(token, ws, bot)).GetProperty("usage");
        summary.GetProperty("workflows").GetInt32().Should().Be(1);
        summary.GetProperty("steps").GetInt32().Should().Be(2);

        using var delete = await _api.DeleteRawAsync(token, ws, bot);
        delete.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await IntegrationsApi.ReadAsync(delete);
        problem.GetProperty("code").GetString().Should().Be("INTEGRATION_IN_USE");
        problem.GetProperty("usedBy").GetArrayLength().Should().Be(2);
        problem.GetProperty("usedBy")[0].GetProperty("workflowRefId").GetGuid().Should().Be(workflow);

        (await _api.GetRawAsync(token, ws, bot)).StatusCode.Should().Be(HttpStatusCode.OK, "a refused delete leaves it in place");
    }

    [SkippableFact]
    public async Task INT_USE_07_UsageFollowsTheCurrentVersion()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        var (token, ws) = await _api.NewWorkspaceAsync();
        var first = IntegrationsApi.RefOf(await _api.CreateAsync(token, ws, IntegrationsApi.Telegram("First")));
        var second = IntegrationsApi.RefOf(await _api.CreateAsync(token, ws, IntegrationsApi.Telegram("Second")));
        var workflowRef = Guid.NewGuid();

        await _api.PublishAsync(token, ws, new WorkflowBuilder("E2E versions", workflowRef).AddManualTrigger().AddTelegram(first, "1", "v1").BuildAndValidate());
        await _api.PublishAsync(token, ws, new WorkflowBuilder("E2E versions", workflowRef).AddManualTrigger().AddTelegram(second, "1", "v2").BuildAndValidate());

        (await _api.UsageAsync(token, ws, first)).GetProperty("usedBy").GetArrayLength().Should().Be(0, "only the current version counts");
        (await _api.UsageAsync(token, ws, second)).GetProperty("usedBy").GetArrayLength().Should().Be(1);

        // So the integration the old version used can go.
        (await _api.DeleteRawAsync(token, ws, first)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        // And rolling back to that version is a publish like any other: refused, the integration is gone.
        using var rollback = await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.ManagementUrl($"/api/workspaces/{ws}/workflows/{workflowRef}/rollback/1"),
            token);
        rollback.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await rollback.Content.ReadAsStringAsync()).Should().Contain("INTEGRATION_NOT_FOUND");
        (await _api.UsageAsync(token, ws, second)).GetProperty("usedBy").GetArrayLength().Should().Be(1, "the refused rollback changed nothing");
    }

    [SkippableFact]
    public async Task INT_USE_08_ADeprecatedWorkflow_ShowsAsDeprecated_AndNoLongerBlocksDelete_ButReinstatingNeedsTheIntegration()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        var (token, ws) = await _api.NewWorkspaceAsync();
        var bot = IntegrationsApi.RefOf(await _api.CreateAsync(token, ws, IntegrationsApi.Telegram("Bot")));
        var workflow = await _api.PublishAsync(token, ws,
            new WorkflowBuilder("E2E deprecate", Guid.NewGuid()).AddManualTrigger().AddTelegram(bot, "1", "hi").BuildAndValidate());

        using (var deprecated = await fixture.SendAsync(HttpMethod.Post, ServicesFixture.ManagementUrl($"/api/workspaces/{ws}/workflows/{workflow}/deprecate"), token))
        {
            deprecated.IsSuccessStatusCode.Should().BeTrue(await deprecated.Content.ReadAsStringAsync());
        }

        var usedBy = (await _api.UsageAsync(token, ws, bot)).GetProperty("usedBy");
        usedBy.GetArrayLength().Should().Be(1);
        usedBy[0].GetProperty("status").GetString().Should().Be("deprecated");

        (await _api.DeleteRawAsync(token, ws, bot)).StatusCode.Should().Be(HttpStatusCode.NoContent, "a deprecated workflow does not hold its integrations");

        using var reinstate = await fixture.SendAsync(HttpMethod.Post, ServicesFixture.ManagementUrl($"/api/workspaces/{ws}/workflows/{workflow}/reinstate"), token);
        reinstate.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await reinstate.Content.ReadAsStringAsync()).Should().Contain("INTEGRATION_NOT_FOUND");
    }

    [SkippableFact]
    public async Task INT_USE_09_ReinstatingWithTheIntegrationStillThere_Works_AndBlocksDeleteAgain()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        var (token, ws) = await _api.NewWorkspaceAsync();
        var bot = IntegrationsApi.RefOf(await _api.CreateAsync(token, ws, IntegrationsApi.Telegram("Bot")));
        var workflow = await _api.PublishAsync(token, ws,
            new WorkflowBuilder("E2E reinstate", Guid.NewGuid()).AddManualTrigger().AddTelegram(bot, "1", "hi").BuildAndValidate());

        (await fixture.SendAsync(HttpMethod.Post, ServicesFixture.ManagementUrl($"/api/workspaces/{ws}/workflows/{workflow}/deprecate"), token))
            .IsSuccessStatusCode.Should().BeTrue();
        (await fixture.SendAsync(HttpMethod.Post, ServicesFixture.ManagementUrl($"/api/workspaces/{ws}/workflows/{workflow}/reinstate"), token))
            .IsSuccessStatusCode.Should().BeTrue();

        (await _api.UsageAsync(token, ws, bot)).GetProperty("usedBy")[0].GetProperty("status").GetString().Should().Be("published");
        (await _api.DeleteRawAsync(token, ws, bot)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [SkippableFact]
    public async Task INT_USE_10_DeletingTheWorkflow_ReleasesItsIntegrations()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        var (token, ws) = await _api.NewWorkspaceAsync();
        var bot = IntegrationsApi.RefOf(await _api.CreateAsync(token, ws, IntegrationsApi.Telegram("Bot")));
        var workflow = await _api.PublishAsync(token, ws,
            new WorkflowBuilder("E2E delete workflow", Guid.NewGuid()).AddManualTrigger().AddTelegram(bot, "1", "hi").BuildAndValidate());

        using (var deleted = await fixture.SendAsync(HttpMethod.Delete, ServicesFixture.ManagementUrl($"/api/workspaces/{ws}/workflows/{workflow}"), token))
        {
            deleted.IsSuccessStatusCode.Should().BeTrue(await deleted.Content.ReadAsStringAsync());
        }

        (await _api.UsageAsync(token, ws, bot)).GetProperty("usedBy").GetArrayLength().Should().Be(0);
        (await _api.GetAsync(token, ws, bot)).GetProperty("usage").GetProperty("workflows").GetInt32().Should().Be(0);
        (await _api.DeleteRawAsync(token, ws, bot)).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [SkippableFact]
    public async Task INT_USE_11_RenamingOrMovingAnIntegrationInUse_IsAllowed()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        var (token, ws) = await _api.NewWorkspaceAsync();
        var key = IntegrationsApi.RefOf(await _api.CreateAsync(token, ws, IntegrationsApi.Bearer("API", "http://127.0.0.1:9")));
        await _api.PublishAsync(token, ws,
            new WorkflowBuilder("E2E in use edits", Guid.NewGuid()).AddManualTrigger().AddWebhook("POST", "/x", null, out _, integrationRef: key).BuildAndValidate());

        var edited = await _api.UpdateAsync(token, ws, key, new { name = "API v2", config = new { baseUrl = "http://127.0.0.1:10", mode = "bearer" }, secrets = new { token = "rotated-token-12345678" } });

        edited.GetProperty("name").GetString().Should().Be("API v2");
        edited.GetProperty("usage").GetProperty("workflows").GetInt32().Should().Be(1, "edits keep the workflows pointing at it");
    }

    [SkippableFact]
    public async Task INT_USE_12_AStepWhoseJsonOmitsTheIntegration_IsRefused()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        var (token, ws) = await _api.NewWorkspaceAsync();

        // The console sends JSON, not the builder: a step with "integrationRef" left out entirely.
        var definition = new WorkflowBuilder("E2E raw json", Guid.NewGuid()).AddManualTrigger().AddTelegram(Guid.NewGuid(), "1", "x").Build();
        var json = JsonSerializer.SerializeToNode(definition, IntegrationsApi.Json)!.AsObject();
        foreach (var node in json["nodes"]!.AsArray())
        {
            node!["config"]?.AsObject().Remove("integrationRef");
        }

        using var response = await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.ManagementUrl($"/api/workspaces/{ws}/workflows"),
            token,
            new { refId = definition.WorkflowRefId, name = definition.Name, definition = json });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("INTEGRATION_REF_MISSING");
    }
}
