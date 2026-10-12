using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Wbskt.E2E.FeatureTests.Fixtures;
using Wbskt.Management.Models.Workflow;
using Wbskt.Workflow.Abstraction.Models;

namespace Wbskt.E2E.FeatureTests.Scenarios.Integrations;

/// <summary>
/// The integrations API as the console calls it, read as raw JSON so a scenario sees exactly what a
/// client would, including anything that should not be there (a secret echoed back).
/// </summary>
internal sealed class IntegrationsApi(ServicesFixture fixture)
{
    /// <summary>Matches the Telegram token shape the engine checks (digits, a colon, 20+ key characters).</summary>
    public const string BotToken = "123456789:AAE-integration-e2e-bot-token-0123";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string Url(Guid workspaceRef, string suffix = "") =>
        ServicesFixture.ManagementUrl($"/api/workspaces/{workspaceRef}/integrations{suffix}");

    /// <summary>A brand-new user in a tenant of their own, with a fresh workspace they administer.</summary>
    public async Task<(string Token, Guid WorkspaceRef)> NewWorkspaceAsync()
    {
        var user = await fixture.CreateUserAsync();
        var workspaceRef = await fixture.CreateWorkspaceAsync(user.Token);
        return (user.Token, workspaceRef);
    }

    public Task<HttpResponseMessage> CreateRawAsync(string token, Guid workspaceRef, object body) =>
        fixture.SendAsync(HttpMethod.Post, Url(workspaceRef), token, body);

    public async Task<JsonElement> CreateAsync(string token, Guid workspaceRef, object body)
    {
        using var response = await CreateRawAsync(token, workspaceRef, body);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await ReadAsync(response);
    }

    public Task<HttpResponseMessage> UpdateRawAsync(string token, Guid workspaceRef, Guid integrationRef, object body) =>
        fixture.SendAsync(HttpMethod.Patch, Url(workspaceRef, $"/{integrationRef}"), token, body);

    public async Task<JsonElement> UpdateAsync(string token, Guid workspaceRef, Guid integrationRef, object body)
    {
        using var response = await UpdateRawAsync(token, workspaceRef, integrationRef, body);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await ReadAsync(response);
    }

    public Task<HttpResponseMessage> GetRawAsync(string token, Guid workspaceRef, Guid integrationRef) =>
        fixture.SendAsync(HttpMethod.Get, Url(workspaceRef, $"/{integrationRef}"), token);

    public async Task<JsonElement> GetAsync(string token, Guid workspaceRef, Guid integrationRef)
    {
        using var response = await GetRawAsync(token, workspaceRef, integrationRef);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await ReadAsync(response);
    }

    public async Task<JsonElement> ListAsync(string token, Guid workspaceRef, string? type = null)
    {
        using var response = await fixture.SendAsync(HttpMethod.Get, Url(workspaceRef, type is null ? "" : $"?type={type}"), token);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await ReadAsync(response);
    }

    public Task<HttpResponseMessage> DeleteRawAsync(string token, Guid workspaceRef, Guid integrationRef) =>
        fixture.SendAsync(HttpMethod.Delete, Url(workspaceRef, $"/{integrationRef}"), token);

    public Task<HttpResponseMessage> TestRawAsync(string token, Guid workspaceRef, object body) =>
        fixture.SendAsync(HttpMethod.Post, Url(workspaceRef, "/test"), token, body);

    public async Task<JsonElement> TestAsync(string token, Guid workspaceRef, object body)
    {
        using var response = await TestRawAsync(token, workspaceRef, body);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await ReadAsync(response);
    }

    public async Task<JsonElement> UsageAsync(string token, Guid workspaceRef, Guid integrationRef)
    {
        using var response = await fixture.SendAsync(HttpMethod.Get, Url(workspaceRef, $"/{integrationRef}/usage"), token);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await ReadAsync(response);
    }

    public Task<HttpResponseMessage> PublishRawAsync(string token, Guid workspaceRef, WorkflowDefinition definition) =>
        fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.ManagementUrl($"/api/workspaces/{workspaceRef}/workflows"),
            token,
            new WorkflowPublishRequest(definition.WorkflowRefId, definition.Name, null, definition));

    public async Task<Guid> PublishAsync(string token, Guid workspaceRef, WorkflowDefinition definition)
    {
        using var response = await PublishRawAsync(token, workspaceRef, definition);
        response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync());
        var published = await response.Content.ReadFromJsonAsync<WorkflowPublishResponse>(Json);
        return published!.RefId;
    }

    public async Task<WorkflowValidationResponse> ValidateAsync(string token, Guid workspaceRef, WorkflowDefinition definition)
    {
        using var response = await fixture.SendAsync(
            HttpMethod.Post,
            ServicesFixture.ManagementUrl($"/api/workspaces/{workspaceRef}/workflows/validate"),
            token,
            new WorkflowPublishRequest(definition.WorkflowRefId, definition.Name, null, definition));
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<WorkflowValidationResponse>(Json))!;
    }

    /// <summary>Starts a manual run and waits for it to finish, returning its RefId and final status.</summary>
    public async Task<(Guid RunRef, string Status)> RunToEndAsync(string token, Guid workspaceRef, Guid workflowRef, Guid triggerNodeId)
    {
        await fixture.StartManualRunAsync(token, workspaceRef, workflowRef, triggerNodeId.ToString(), idempotencyKey: null);
        var runRef = await fixture.WaitForFirstRunAsync(token, workspaceRef, workflowRef, TimeSpan.FromSeconds(30));
        runRef.Should().NotBe(Guid.Empty, "the manual start creates a run");
        var summary = await fixture.WaitForRunTerminalAsync(token, workspaceRef, runRef, TimeSpan.FromSeconds(60));
        summary.Should().NotBeNull("the run finishes");
        return (runRef, summary!.Status);
    }

    /// <summary>The error code a failed step recorded in the run's history, or null when none failed.</summary>
    public async Task<string?> FailedStepPayloadAsync(string token, Guid workspaceRef, Guid runRef)
    {
        var history = await fixture.GetHistoryAsync(token, workspaceRef, runRef);
        return history.FirstOrDefault(e => e.EventKind == "NodeFailed")?.PayloadJson;
    }

    public static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        string text = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    public static Guid RefOf(JsonElement integration) => integration.GetProperty("refId").GetGuid();

    // ── Bodies for each type ────────────────────────────────────────────────────────

    public static object Telegram(string name, string botToken = BotToken) => new
    {
        type = "telegram",
        name,
        config = new { },
        secrets = new { botToken }
    };

    public static object Discord(string name, string webhookUrl) => new
    {
        type = "discord",
        name,
        secrets = new { webhookUrl }
    };

    public static object Smtp(string name, int port, string? userName = "mailer", string? password = "relay-password-1234", string fromAddress = "alerts@wbskt.test") => new
    {
        type = "smtp",
        name,
        config = new { host = "127.0.0.1", port, userName, fromAddress, fromName = "Wbskt Alerts" },
        secrets = password is null ? null : new { password }
    };

    public static object Ntfy(string name, string serverUrl, string topic = "garage-door-alerts", string? accessToken = null) => new
    {
        type = "ntfy",
        name,
        config = new { serverUrl },
        secrets = new { topic, accessToken }
    };

    public static object Bearer(string name, string baseUrl, string token = "bearer-token-abcdef123456") => new
    {
        type = "httpAuth",
        name,
        config = new { baseUrl, mode = "bearer" },
        secrets = new { token }
    };

    public static object HeaderAuth(string name, string baseUrl, string headerName = "X-Api-Key", string headerValue = "header-key-0987654321") => new
    {
        type = "httpAuth",
        name,
        config = new { baseUrl, mode = "header", headerName },
        secrets = new { headerValue }
    };

    public static object HomeAssistant(string name, string baseUrl, string token = "ha-long-lived-token-abcdef") => new
    {
        type = "httpAuth",
        preset = "homeAssistant",
        name,
        config = new { baseUrl },
        secrets = new { token }
    };

    public static string Unique(string prefix) => $"{prefix} {Guid.NewGuid():N}"[..(prefix.Length + 9)];
}
