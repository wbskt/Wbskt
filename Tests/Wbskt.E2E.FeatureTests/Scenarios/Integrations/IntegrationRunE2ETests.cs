using System.Text.Json;
using FluentAssertions;
using Wbskt.E2E.FeatureTests.Fixtures;

namespace Wbskt.E2E.FeatureTests.Scenarios.Integrations;

/// <summary>
/// Workflow runs sending through integrations: each step kind reaches its service with the saved
/// credential, a rotated credential applies without republishing, and Call a URL keeps an
/// integration's credential on the origin it was saved for.
/// </summary>
[Collection(E2ECollection.Name)]
public sealed class IntegrationRunE2ETests(ServicesFixture fixture)
{
    private readonly IntegrationsApi _api = new(fixture);

    [SkippableFact]
    public async Task INT_RUN_01_ATelegramStep_SendsWithTheSavedBot_AndMarksItUsed()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        await using var telegram = FakeHttpServer.StartTelegram();
        var (token, ws) = await _api.NewWorkspaceAsync();
        var bot = IntegrationsApi.RefOf(await _api.CreateAsync(token, ws, IntegrationsApi.Telegram("Bot")));

        var definition = new WorkflowBuilder("E2E integration telegram", Guid.NewGuid())
            .AddManualTrigger(out var trigger)
            .AddTelegram(bot, "-1001", "The garage door is open")
            .BuildAndValidate();
        var workflow = await _api.PublishAsync(token, ws, definition);

        var (_, status) = await _api.RunToEndAsync(token, ws, workflow, trigger);

        status.Should().Be("Succeeded");
        var sent = telegram.Requests.Should().ContainSingle(r => r.Path == $"/telegram/bot{IntegrationsApi.BotToken}/sendMessage").Subject;
        using (var body = JsonDocument.Parse(sent.Body))
        {
            body.RootElement.GetProperty("chat_id").GetString().Should().Be("-1001");
            body.RootElement.GetProperty("text").GetString().Should().Be("The garage door is open");
        }

        (await ServicesFixture.PollAsync(
                async () => (await _api.GetAsync(token, ws, bot)).GetProperty("lastUsedAt").ValueKind == JsonValueKind.String,
                TimeSpan.FromSeconds(15),
                TimeSpan.FromMilliseconds(500)))
            .Should().BeTrue("a successful send marks the integration used");
    }

    [SkippableFact]
    public async Task INT_RUN_02_ARotatedToken_AppliesToTheNextRun_WithoutRepublishing()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        await using var telegram = FakeHttpServer.StartTelegram();
        var (token, ws) = await _api.NewWorkspaceAsync();
        var bot = IntegrationsApi.RefOf(await _api.CreateAsync(token, ws, IntegrationsApi.Telegram("Bot")));

        var definition = new WorkflowBuilder("E2E integration rotation", Guid.NewGuid())
            .AddManualTrigger(out var trigger)
            .AddTelegram(bot, "-1001", "hello")
            .BuildAndValidate();
        var workflow = await _api.PublishAsync(token, ws, definition);
        (await _api.RunToEndAsync(token, ws, workflow, trigger)).Status.Should().Be("Succeeded");

        const string rotated = "987654321:BBF-rotated-e2e-bot-token-9876";
        await _api.UpdateAsync(token, ws, bot, new { secrets = new { botToken = rotated } });

        await fixture.StartManualRunAsync(token, ws, workflow, trigger.ToString(), idempotencyKey: null);
        (await telegram.WaitForAsync(r => r.Path == $"/telegram/bot{rotated}/sendMessage"))
            .Should().NotBeNull("the next run opens the secret as it is now, not as it was at publish");
    }

    [SkippableFact]
    public async Task INT_RUN_03_ATelegramRefusal_FailsTheRun_WithAStepCode_AndNoToken()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        await using var telegram = FakeHttpServer.StartTelegram();
        telegram.Respond = _ => (400, """{"ok":false,"description":"Bad Request: chat not found"}""");
        var (token, ws) = await _api.NewWorkspaceAsync();
        var bot = IntegrationsApi.RefOf(await _api.CreateAsync(token, ws, IntegrationsApi.Telegram("Bot")));

        var definition = new WorkflowBuilder("E2E integration telegram failure", Guid.NewGuid())
            .AddManualTrigger(out var trigger)
            .AddTelegram(bot, "nope", "hello")
            .BuildAndValidate();
        var workflow = await _api.PublishAsync(token, ws, definition);

        var (run, status) = await _api.RunToEndAsync(token, ws, workflow, trigger);

        status.Should().Be("Failed");
        var history = await fixture.GetHistoryAsync(token, ws, run);
        history.Should().Contain(e => e.PayloadJson != null && e.PayloadJson.Contains("TELEGRAM_HTTP_ERROR"));
        history.Should().NotContain(e => e.PayloadJson != null && e.PayloadJson.Contains(IntegrationsApi.BotToken), "a run's history never carries the token");
    }

    [SkippableFact]
    public async Task INT_RUN_04_AnEmailStep_SendsThroughTheRelay_SignedIn()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        await using var smtp = FakeSmtpServer.Start("mailer", "relay-password-1234");
        var (token, ws) = await _api.NewWorkspaceAsync();
        var relay = IntegrationsApi.RefOf(await _api.CreateAsync(token, ws, IntegrationsApi.Smtp("Relay", smtp.Port)));

        var definition = new WorkflowBuilder("E2E integration email", Guid.NewGuid())
            .AddManualTrigger(out var trigger)
            .AddEmail(relay, "owner@wbskt.test", "Door open", "The garage door has been open for 10 minutes.")
            .BuildAndValidate();
        var workflow = await _api.PublishAsync(token, ws, definition);

        var (_, status) = await _api.RunToEndAsync(token, ws, workflow, trigger);

        status.Should().Be("Succeeded");
        var mail = smtp.Messages.Should().ContainSingle().Subject;
        mail.UserName.Should().Be("mailer");
        mail.From.Should().Be("alerts@wbskt.test");
        mail.To.Should().Equal("owner@wbskt.test");
        mail.Data.Should().Contain("Subject: Door open").And.Contain("open for 10 minutes").And.Contain("Wbskt Alerts");
    }

    [SkippableFact]
    public async Task INT_RUN_05_AnEmailToARefusedRecipient_FailsTheRun()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        await using var smtp = FakeSmtpServer.Start("mailer", "relay-password-1234");
        smtp.RefusedRecipients.Add("gone@wbskt.test");
        var (token, ws) = await _api.NewWorkspaceAsync();
        var relay = IntegrationsApi.RefOf(await _api.CreateAsync(token, ws, IntegrationsApi.Smtp("Relay", smtp.Port)));

        var definition = new WorkflowBuilder("E2E integration email refused", Guid.NewGuid())
            .AddManualTrigger(out var trigger)
            .AddEmail(relay, "gone@wbskt.test", "Door open", "body")
            .BuildAndValidate();
        var workflow = await _api.PublishAsync(token, ws, definition);

        var (run, status) = await _api.RunToEndAsync(token, ws, workflow, trigger);

        status.Should().Be("Failed");
        (await fixture.GetHistoryAsync(token, ws, run))
            .Should().Contain(e => e.PayloadJson != null && e.PayloadJson.Contains("EMAIL_RECIPIENT_REFUSED"));
        smtp.Messages.Should().BeEmpty();
    }

    [SkippableFact]
    public async Task INT_RUN_06_ADiscordStep_PostsWithoutParsingMentions()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        await using var discord = FakeHttpServer.Start();
        discord.Respond = _ => (204, string.Empty);
        var (token, ws) = await _api.NewWorkspaceAsync();
        var hook = IntegrationsApi.RefOf(await _api.CreateAsync(token, ws, IntegrationsApi.Discord("Ops", $"{discord.BaseUrl}/api/webhooks/77/hook-token-abcdef")));

        var definition = new WorkflowBuilder("E2E integration discord", Guid.NewGuid())
            .AddManualTrigger(out var trigger)
            .AddDiscord(hook, "@everyone the door is open", username: "Garage")
            .BuildAndValidate();
        var workflow = await _api.PublishAsync(token, ws, definition);

        var (_, status) = await _api.RunToEndAsync(token, ws, workflow, trigger);

        status.Should().Be("Succeeded");
        var post = discord.Requests.Should().ContainSingle().Subject;
        post.Method.Should().Be("POST");
        post.Path.Should().Be("/api/webhooks/77/hook-token-abcdef");
        using var body = JsonDocument.Parse(post.Body);
        body.RootElement.GetProperty("content").GetString().Should().Be("@everyone the door is open");
        body.RootElement.GetProperty("username").GetString().Should().Be("Garage");
        body.RootElement.GetProperty("allowed_mentions").GetProperty("parse").GetArrayLength().Should().Be(0, "device data must not be able to ping @everyone");
    }

    [SkippableFact]
    public async Task INT_RUN_07_AnNtfyStep_PublishesTitlePriorityAndTags_WithTheAccessToken()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        await using var ntfy = FakeHttpServer.Start();
        var (token, ws) = await _api.NewWorkspaceAsync();
        var phone = IntegrationsApi.RefOf(await _api.CreateAsync(token, ws, IntegrationsApi.Ntfy("Phone", ntfy.BaseUrl, topic: "house-alerts", accessToken: "tk_e2e_access_token")));

        var definition = new WorkflowBuilder("E2E integration ntfy", Guid.NewGuid())
            .AddManualTrigger(out var trigger)
            .AddNtfy(phone, "Door open", title: "Garage", priority: 4, tags: ["warning", "door"])
            .BuildAndValidate();
        var workflow = await _api.PublishAsync(token, ws, definition);

        var (_, status) = await _api.RunToEndAsync(token, ws, workflow, trigger);

        status.Should().Be("Succeeded");
        var published = ntfy.Requests.Should().ContainSingle().Subject;
        published.Header("Authorization").Should().Be("Bearer tk_e2e_access_token");
        using var body = JsonDocument.Parse(published.Body);
        body.RootElement.GetProperty("topic").GetString().Should().Be("house-alerts");
        body.RootElement.GetProperty("message").GetString().Should().Be("Door open");
        body.RootElement.GetProperty("title").GetString().Should().Be("Garage");
        body.RootElement.GetProperty("priority").GetInt32().Should().Be(4);
        body.RootElement.GetProperty("tags").EnumerateArray().Select(t => t.GetString()).Should().Equal("warning", "door");
    }

    [SkippableFact]
    public async Task INT_RUN_08_CallAUrl_WithAnIntegration_GoesToTheBaseUrl_WithTheCredentialAndHeaders()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        await using var house = FakeHttpServer.Start();
        var (token, ws) = await _api.NewWorkspaceAsync();
        var ha = IntegrationsApi.RefOf(await _api.CreateAsync(token, ws, IntegrationsApi.HomeAssistant("House", house.BaseUrl, token: "ha-token-1234567890")));

        var definition = new WorkflowBuilder("E2E integration webhook", Guid.NewGuid())
            .AddManualTrigger(out var trigger)
            .AddWebhook(
                "POST",
                "/api/services/light/turn_on",
                JsonSerializer.SerializeToElement(new { entity_id = "light.garage" }),
                out _,
                integrationRef: ha,
                headers: new Dictionary<string, string> { ["X-Request-Source"] = "wbskt-e2e" })
            .BuildAndValidate();
        var workflow = await _api.PublishAsync(token, ws, definition);

        var (_, status) = await _api.RunToEndAsync(token, ws, workflow, trigger);

        status.Should().Be("Succeeded");
        var call = house.Requests.Should().ContainSingle().Subject;
        call.Method.Should().Be("POST");
        call.Path.Should().Be("/api/services/light/turn_on");
        call.Header("Authorization").Should().Be("Bearer ha-token-1234567890");
        call.Header("X-Request-Source").Should().Be("wbskt-e2e");
        call.Body.Should().Contain("light.garage");
    }

    [SkippableFact]
    public async Task INT_RUN_09_CallAUrl_HeaderModeAndAnAbsoluteUrlOnTheSameOrigin_Works()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        await using var api = FakeHttpServer.Start();
        var (token, ws) = await _api.NewWorkspaceAsync();
        var key = IntegrationsApi.RefOf(await _api.CreateAsync(token, ws, IntegrationsApi.HeaderAuth("Status API", api.BaseUrl + "/v1")));

        var definition = new WorkflowBuilder("E2E integration webhook absolute", Guid.NewGuid())
            .AddManualTrigger(out var trigger)
            .AddWebhook("GET", api.BaseUrl + "/v1/status?verbose=1", null, out _, integrationRef: key)
            .BuildAndValidate();
        var workflow = await _api.PublishAsync(token, ws, definition);

        (await _api.RunToEndAsync(token, ws, workflow, trigger)).Status.Should().Be("Succeeded");
        var call = api.Requests.Should().ContainSingle().Subject;
        call.Path.Should().Be("/v1/status");
        call.Query.Should().Be("?verbose=1");
        call.Header("X-Api-Key").Should().Be("header-key-0987654321");
        call.Header("Authorization").Should().BeNull();
    }

    [SkippableFact]
    public async Task INT_RUN_10_WhenTheBaseUrlMoves_AnAbsoluteUrlOnTheOldOrigin_FailsWithoutSendingTheCredential()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        await using var oldHost = FakeHttpServer.Start();
        await using var newHost = FakeHttpServer.Start();
        var (token, ws) = await _api.NewWorkspaceAsync();
        var key = IntegrationsApi.RefOf(await _api.CreateAsync(token, ws, IntegrationsApi.Bearer("API", oldHost.BaseUrl)));

        var definition = new WorkflowBuilder("E2E integration webhook moved", Guid.NewGuid())
            .AddManualTrigger(out var trigger)
            .AddWebhook("POST", oldHost.BaseUrl + "/hook", null, out _, integrationRef: key)
            .BuildAndValidate();
        var workflow = await _api.PublishAsync(token, ws, definition);

        // Moving the integration is allowed while it is in use: the step's relative URLs follow it.
        await _api.UpdateAsync(token, ws, key, new { config = new { baseUrl = newHost.BaseUrl, mode = "bearer" } });

        var (run, status) = await _api.RunToEndAsync(token, ws, workflow, trigger);

        status.Should().Be("Failed");
        (await fixture.GetHistoryAsync(token, ws, run))
            .Should().Contain(e => e.PayloadJson != null && e.PayloadJson.Contains("WEBHOOK_ORIGIN_MISMATCH"));
        oldHost.Requests.Should().BeEmpty("the credential is never sent to an origin it was not saved for");
        newHost.Requests.Should().BeEmpty();
    }

    [SkippableFact]
    public async Task INT_RUN_11_CallAUrl_WithoutAnIntegration_StillWorks_AndCarriesCustomHeaders()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        await using var api = FakeHttpServer.Start();
        var (token, ws) = await _api.NewWorkspaceAsync();

        var definition = new WorkflowBuilder("E2E plain webhook", Guid.NewGuid())
            .AddManualTrigger(out var trigger)
            .AddWebhook("PUT", api.BaseUrl + "/plain", JsonSerializer.SerializeToElement(new { a = 1 }), out _, headers: new Dictionary<string, string> { ["X-Trace"] = "t-1" })
            .BuildAndValidate();
        var workflow = await _api.PublishAsync(token, ws, definition);

        (await _api.RunToEndAsync(token, ws, workflow, trigger)).Status.Should().Be("Succeeded");
        var call = api.Requests.Should().ContainSingle().Subject;
        call.Method.Should().Be("PUT");
        call.Header("X-Trace").Should().Be("t-1");
        call.Header("Authorization").Should().BeNull();
    }

    [SkippableFact]
    public async Task INT_RUN_12_OneWorkflow_SendsThroughEveryKind()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        await using var telegram = FakeHttpServer.StartTelegram();
        await using var discord = FakeHttpServer.Start();
        await using var ntfy = FakeHttpServer.Start();
        await using var api = FakeHttpServer.Start();
        await using var smtp = FakeSmtpServer.Start("mailer", "relay-password-1234");
        var (token, ws) = await _api.NewWorkspaceAsync();

        var bot = IntegrationsApi.RefOf(await _api.CreateAsync(token, ws, IntegrationsApi.Telegram("Bot")));
        var hook = IntegrationsApi.RefOf(await _api.CreateAsync(token, ws, IntegrationsApi.Discord("Ops", $"{discord.BaseUrl}/api/webhooks/1/abcdefghijkl")));
        var phone = IntegrationsApi.RefOf(await _api.CreateAsync(token, ws, IntegrationsApi.Ntfy("Phone", ntfy.BaseUrl)));
        var relay = IntegrationsApi.RefOf(await _api.CreateAsync(token, ws, IntegrationsApi.Smtp("Relay", smtp.Port)));
        var key = IntegrationsApi.RefOf(await _api.CreateAsync(token, ws, IntegrationsApi.Bearer("API", api.BaseUrl)));

        var definition = new WorkflowBuilder("E2E integration every kind", Guid.NewGuid())
            .AddManualTrigger(out var trigger)
            .AddTelegram(bot, "1", "one")
            .AddDiscord(hook, "two")
            .AddNtfy(phone, "three")
            .AddEmail(relay, "owner@wbskt.test", "four", "four")
            .AddWebhook("POST", "/five", null, out _, integrationRef: key)
            .BuildAndValidate();
        var workflow = await _api.PublishAsync(token, ws, definition);

        (await _api.RunToEndAsync(token, ws, workflow, trigger)).Status.Should().Be("Succeeded");
        telegram.Requests.Should().Contain(r => r.Path.EndsWith("/sendMessage"));
        discord.Requests.Should().ContainSingle();
        ntfy.Requests.Should().ContainSingle();
        smtp.Messages.Should().ContainSingle();
        api.Requests.Should().ContainSingle(r => r.Path == "/five");

        // Each integration now reads as used by this one workflow.
        foreach (var refId in new[] { bot, hook, phone, relay, key })
        {
            var usage = (await _api.GetAsync(token, ws, refId)).GetProperty("usage");
            usage.GetProperty("workflows").GetInt32().Should().Be(1);
            usage.GetProperty("steps").GetInt32().Should().Be(1);
        }
    }
}
