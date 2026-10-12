using System.Net;
using System.Text.Json;
using FluentAssertions;
using Wbskt.E2E.FeatureTests.Fixtures;

namespace Wbskt.E2E.FeatureTests.Scenarios.Integrations;

/// <summary>
/// "Test connection": each type tried against a local stand-in for its service, the outcomes the
/// console shows, and the rule that only a test of the saved integration is recorded on it.
/// </summary>
[Collection(E2ECollection.Name)]
public sealed class IntegrationTestEndpointE2ETests(ServicesFixture fixture)
{
    private readonly IntegrationsApi _api = new(fixture);

    [SkippableFact]
    public async Task INT_TEST_01_Telegram_ChecksTheToken_RecordsTheBot_AndSendsToAChatWhenAsked()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        await using var telegram = FakeHttpServer.StartTelegram();
        var (token, ws) = await _api.NewWorkspaceAsync();
        var refId = IntegrationsApi.RefOf(await _api.CreateAsync(token, ws, IntegrationsApi.Telegram("Bot")));

        var result = await _api.TestAsync(token, ws, new { refId });

        result.GetProperty("ok").GetBoolean().Should().BeTrue(result.ToString());
        result.GetProperty("metadata").GetProperty("botUsername").GetString().Should().Be("garage_bot");
        telegram.Requests.Should().ContainSingle(r => r.Path == $"/telegram/bot{IntegrationsApi.BotToken}/getMe");
        telegram.Requests.Should().NotContain(r => r.Path.EndsWith("/sendMessage"), "no chat was named, so nothing is posted");

        var saved = await _api.GetAsync(token, ws, refId);
        saved.GetProperty("lastTest").GetProperty("ok").GetBoolean().Should().BeTrue();
        saved.GetProperty("metadata").GetProperty("botName").GetString().Should().Be("Garage Bot");

        var withChat = await _api.TestAsync(token, ws, new { refId, options = new { chatId = "-100200300" } });
        withChat.GetProperty("ok").GetBoolean().Should().BeTrue(withChat.ToString());
        var sent = telegram.Requests.Should().ContainSingle(r => r.Path.EndsWith("/sendMessage")).Subject;
        sent.Body.Should().Contain("\"chat_id\":\"-100200300\"");
    }

    [SkippableFact]
    public async Task INT_TEST_02_Telegram_ARejectedTokenOrChat_IsAFailedTest_NotAnError()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        await using var telegram = FakeHttpServer.StartTelegram();
        var (token, ws) = await _api.NewWorkspaceAsync();
        var refId = IntegrationsApi.RefOf(await _api.CreateAsync(token, ws, IntegrationsApi.Telegram("Bot")));

        telegram.Respond = _ => (401, """{"ok":false,"error_code":401,"description":"Unauthorized"}""");
        var revoked = await _api.TestAsync(token, ws, new { refId });
        revoked.GetProperty("ok").GetBoolean().Should().BeFalse();
        revoked.GetProperty("code").GetString().Should().Be("INTEGRATION_AUTH_FAILED");
        revoked.GetProperty("message").GetString().Should().NotContain(IntegrationsApi.BotToken);

        var saved = await _api.GetAsync(token, ws, refId);
        saved.GetProperty("lastTest").GetProperty("ok").GetBoolean().Should().BeFalse();
        saved.GetProperty("lastTest").GetProperty("code").GetString().Should().Be("INTEGRATION_AUTH_FAILED");

        telegram.Respond = r => r.Path.EndsWith("/getMe")
            ? (200, """{"ok":true,"result":{"username":"garage_bot"}}""")
            : (400, """{"ok":false,"description":"Bad Request: chat not found"}""");
        var noChat = await _api.TestAsync(token, ws, new { refId, options = new { chatId = "404" } });
        noChat.GetProperty("ok").GetBoolean().Should().BeFalse();
        noChat.GetProperty("code").GetString().Should().Be("INTEGRATION_HTTP_ERROR");
        noChat.GetProperty("message").GetString().Should().Contain("chat not found");
    }

    [SkippableFact]
    public async Task INT_TEST_03_Discord_ReadsTheWebhook_WithoutPosting()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        await using var discord = FakeHttpServer.Start();
        discord.Respond = _ => (200, """{"name":"Wbskt alerts","channel_id":"998877"}""");
        var (token, ws) = await _api.NewWorkspaceAsync();
        var refId = IntegrationsApi.RefOf(await _api.CreateAsync(token, ws, IntegrationsApi.Discord("Ops", $"{discord.BaseUrl}/api/webhooks/1/hook-token-abcdef")));

        var result = await _api.TestAsync(token, ws, new { refId });

        result.GetProperty("ok").GetBoolean().Should().BeTrue(result.ToString());
        result.GetProperty("metadata").GetProperty("channelId").GetString().Should().Be("998877");
        discord.Requests.Should().ContainSingle().Which.Method.Should().Be("GET");

        discord.Respond = _ => (404, """{"message":"Unknown Webhook"}""");
        var deleted = await _api.TestAsync(token, ws, new { refId });
        deleted.GetProperty("code").GetString().Should().Be("INTEGRATION_AUTH_FAILED");
    }

    [SkippableFact]
    public async Task INT_TEST_04_Ntfy_PublishesATestNotification()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        await using var ntfy = FakeHttpServer.Start();
        var (token, ws) = await _api.NewWorkspaceAsync();
        var refId = IntegrationsApi.RefOf(await _api.CreateAsync(token, ws, IntegrationsApi.Ntfy("Phone", ntfy.BaseUrl, topic: "e2e-topic-1234")));

        var result = await _api.TestAsync(token, ws, new { refId });

        result.GetProperty("ok").GetBoolean().Should().BeTrue(result.ToString());
        var published = ntfy.Requests.Should().ContainSingle().Subject;
        published.Method.Should().Be("POST");
        published.Path.Should().Be("/");
        published.Header("Authorization").Should().BeNull("this topic has no access token");
        using var body = JsonDocument.Parse(published.Body);
        body.RootElement.GetProperty("topic").GetString().Should().Be("e2e-topic-1234");

        ntfy.Respond = _ => (403, """{"error":"forbidden"}""");
        var forbidden = await _api.TestAsync(token, ws, new { refId });
        forbidden.GetProperty("code").GetString().Should().Be("INTEGRATION_AUTH_FAILED");
    }

    [SkippableFact]
    public async Task INT_TEST_05_HomeAssistant_CallsApiConfig_WithTheBearerToken()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        await using var house = FakeHttpServer.Start();
        house.Respond = _ => (200, """{"version":"2026.10.1","location_name":"Home"}""");
        var (token, ws) = await _api.NewWorkspaceAsync();
        var refId = IntegrationsApi.RefOf(await _api.CreateAsync(token, ws, IntegrationsApi.HomeAssistant("House", house.BaseUrl, token: "ha-token-1234567890")));

        var result = await _api.TestAsync(token, ws, new { refId });

        result.GetProperty("ok").GetBoolean().Should().BeTrue(result.ToString());
        result.GetProperty("metadata").GetProperty("version").GetString().Should().Be("2026.10.1");
        var call = house.Requests.Should().ContainSingle().Subject;
        call.Path.Should().Be("/api/config");
        call.Header("Authorization").Should().Be("Bearer ha-token-1234567890");
    }

    [SkippableFact]
    public async Task INT_TEST_06_HttpAuth_HeaderMode_SendsTheNamedHeader_AndBasicSendsCredentials()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        await using var api = FakeHttpServer.Start();
        var (token, ws) = await _api.NewWorkspaceAsync();

        var header = await _api.TestAsync(token, ws, new
        {
            type = "httpAuth",
            config = new { baseUrl = api.BaseUrl + "/v1", mode = "header", headerName = "X-Api-Key" },
            secrets = new { headerValue = "key-123" }
        });
        header.GetProperty("ok").GetBoolean().Should().BeTrue(header.ToString());
        api.Requests[^1].Path.Should().Be("/v1");
        api.Requests[^1].Header("X-Api-Key").Should().Be("key-123");

        var basic = await _api.TestAsync(token, ws, new
        {
            type = "httpAuth",
            config = new { baseUrl = api.BaseUrl, mode = "basic", userName = "svc" },
            secrets = new { password = "pw" }
        });
        basic.GetProperty("ok").GetBoolean().Should().BeTrue(basic.ToString());
        api.Requests[^1].Header("Authorization").Should().Be("Basic " + Convert.ToBase64String("svc:pw"u8.ToArray()));

        api.Respond = _ => (500, "{}");
        var down = await _api.TestAsync(token, ws, new { type = "httpAuth", config = new { baseUrl = api.BaseUrl, mode = "bearer" }, secrets = new { token = "t" } });
        down.GetProperty("code").GetString().Should().Be("INTEGRATION_HTTP_ERROR");
    }

    [SkippableFact]
    public async Task INT_TEST_07_Smtp_SignsIn_AndSendsATestEmailWhenAsked()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        await using var smtp = FakeSmtpServer.Start("mailer", "relay-password-1234");
        smtp.RefusedRecipients.Add("nobody@wbskt.test");
        var (token, ws) = await _api.NewWorkspaceAsync();
        var refId = IntegrationsApi.RefOf(await _api.CreateAsync(token, ws, IntegrationsApi.Smtp("Relay", smtp.Port)));

        var signIn = await _api.TestAsync(token, ws, new { refId });
        signIn.GetProperty("ok").GetBoolean().Should().BeTrue(signIn.ToString());
        smtp.Authentications.Should().Be(1);
        smtp.Messages.Should().BeEmpty("without sendTo the test only signs in");

        var sendTo = await _api.TestAsync(token, ws, new { refId, options = new { sendTo = "owner@wbskt.test" } });
        sendTo.GetProperty("ok").GetBoolean().Should().BeTrue(sendTo.ToString());
        var mail = smtp.Messages.Should().ContainSingle().Subject;
        mail.From.Should().Be("alerts@wbskt.test");
        mail.To.Should().Equal("owner@wbskt.test");
        mail.Data.Should().Contain("Subject: Test email from Wbskt");

        var refused = await _api.TestAsync(token, ws, new { refId, options = new { sendTo = "nobody@wbskt.test" } });
        refused.GetProperty("code").GetString().Should().Be("INTEGRATION_RECIPIENT_REFUSED");

        var badAddress = await _api.TestAsync(token, ws, new { refId, options = new { sendTo = "not an address" } });
        badAddress.GetProperty("code").GetString().Should().Be("INTEGRATION_ADDRESS_INVALID");

        // The wrong password, tried as an unsaved edit.
        var wrongPassword = await _api.TestAsync(token, ws, new { refId, secrets = new { password = "wrong-password" } });
        wrongPassword.GetProperty("code").GetString().Should().Be("INTEGRATION_AUTH_FAILED");
    }

    [SkippableFact]
    public async Task INT_TEST_08_ANothingListening_IsUnreachable()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        var (token, ws) = await _api.NewWorkspaceAsync();
        int closedPort = FakeHttpServer.FreePort();

        var http = await _api.TestAsync(token, ws, new { type = "httpAuth", config = new { baseUrl = $"http://127.0.0.1:{closedPort}", mode = "bearer" }, secrets = new { token = "t" } });
        http.GetProperty("ok").GetBoolean().Should().BeFalse();
        http.GetProperty("code").GetString().Should().Be("INTEGRATION_UNREACHABLE");

        var smtp = await _api.TestAsync(token, ws, new { type = "smtp", config = new { host = "127.0.0.1", port = closedPort, fromAddress = "a@wbskt.test" } });
        smtp.GetProperty("code").GetString().Should().Be("INTEGRATION_UNREACHABLE");
    }

    [SkippableFact]
    public async Task INT_TEST_09_OnlyATestOfTheSavedIntegration_IsRecorded()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        await using var api = FakeHttpServer.Start();
        var (token, ws) = await _api.NewWorkspaceAsync();
        var refId = IntegrationsApi.RefOf(await _api.CreateAsync(token, ws, IntegrationsApi.Bearer("API", api.BaseUrl)));

        // An unsaved edit (other settings, or another secret) says nothing about what is saved.
        api.Respond = _ => (401, "{}");
        (await _api.TestAsync(token, ws, new { refId, secrets = new { token = "draft-token" } }))
            .GetProperty("ok").GetBoolean().Should().BeFalse();
        (await _api.TestAsync(token, ws, new { refId, config = new { baseUrl = api.BaseUrl + "/draft", mode = "bearer" } }))
            .GetProperty("ok").GetBoolean().Should().BeFalse();
        (await _api.GetAsync(token, ws, refId)).GetProperty("lastTest").ValueKind.Should().Be(JsonValueKind.Null);

        // The unsaved secret was the one sent, and the saved one was used when none was given.
        api.Requests[0].Header("Authorization").Should().Be("Bearer draft-token");
        api.Requests[1].Header("Authorization").Should().Be("Bearer bearer-token-abcdef123456");
        api.Requests[1].Path.Should().Be("/draft");

        api.Respond = _ => (200, "{}");
        (await _api.TestAsync(token, ws, new { refId })).GetProperty("ok").GetBoolean().Should().BeTrue();
        var tested = await _api.GetAsync(token, ws, refId);
        tested.GetProperty("lastTest").GetProperty("ok").GetBoolean().Should().BeTrue();

        // Changing the settings clears the result: it described other settings.
        var moved = await _api.UpdateAsync(token, ws, refId, new { config = new { baseUrl = api.BaseUrl + "/v2", mode = "bearer" } });
        moved.GetProperty("lastTest").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [SkippableFact]
    public async Task INT_TEST_10_ATestBeforeSaving_ValidatesLikeACreate()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        var (token, ws) = await _api.NewWorkspaceAsync();

        using var noType = await _api.TestRawAsync(token, ws, new { config = new { } });
        noType.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ServicesFixture.ReadErrorCodeAsync(noType)).Should().Be("INTEGRATION_TYPE_INVALID");

        using var badToken = await _api.TestRawAsync(token, ws, new { type = "telegram", secrets = new { botToken = "nope" } });
        badToken.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ServicesFixture.ReadErrorCodeAsync(badToken)).Should().Be("INTEGRATION_SECRET_INVALID");

        var refId = IntegrationsApi.RefOf(await _api.CreateAsync(token, ws, IntegrationsApi.Telegram("Bot")));
        using var otherType = await _api.TestRawAsync(token, ws, new { refId, type = "discord" });
        otherType.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ServicesFixture.ReadErrorCodeAsync(otherType)).Should().Be("INTEGRATION_TYPE_IMMUTABLE");
    }

    [SkippableFact]
    public async Task INT_TEST_11_TestsAreRateLimitedPerWorkspace()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        var (token, ws) = await _api.NewWorkspaceAsync();
        var body = new { type = "telegram", secrets = new { botToken = "nope" } };

        var statuses = new List<HttpStatusCode>();
        for (int i = 0; i < 25; i++)
        {
            using var response = await _api.TestRawAsync(token, ws, body);
            statuses.Add(response.StatusCode);
        }

        statuses.Take(20).Should().OnlyContain(s => s == HttpStatusCode.BadRequest, "the first 20 in a minute are served");
        statuses.Skip(20).Should().OnlyContain(s => s == HttpStatusCode.TooManyRequests);

        // Another workspace has its own budget.
        var other = await fixture.CreateWorkspaceAsync(token);
        using var elsewhere = await _api.TestRawAsync(token, other, body);
        elsewhere.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
