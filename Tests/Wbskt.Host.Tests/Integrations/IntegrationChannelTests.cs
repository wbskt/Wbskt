using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Wbskt.Workflow.Abstraction.Integrations;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
using Wbskt.Workflow.Engine.Contracts;
using Wbskt.Workflow.Integrations;
using Wbskt.Workflow.Runtime;
using static Wbskt.Host.Tests.Integrations.IntegrationTestKit;
using static Wbskt.Host.Tests.NodeExecutors.Actions.NotificationExecutorTests;

namespace Wbskt.Host.Tests.Integrations;

public sealed class IntegrationChannelTests
{
    private const string DiscordWebhook = "https://discord.com/api/webhooks/123/very-secret-webhook-token";

    // ------------------------------------------------------------- telegram

    [Fact]
    public async Task Telegram_test_asks_getMe_and_reports_the_bot()
    {
        var handler = new FakeHandler(_ => Json(HttpStatusCode.OK, "{\"ok\":true,\"result\":{\"username\":\"wbskt_bot\",\"first_name\":\"Wbskt\"}}"));

        var result = await Telegram(handler).TestAsync(TelegramIntegration(), null, CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal(HttpMethod.Get, handler.LastRequest!.Method);
        Assert.Equal($"https://tg.test/bot{BotToken}/getMe", handler.LastUri);
        Assert.Equal("wbskt_bot", result.Metadata!.Value.GetProperty("botUsername").GetString());
        Assert.Equal("Wbskt", result.Metadata.Value.GetProperty("botName").GetString());
    }

    [Fact]
    public async Task Telegram_test_with_a_chat_also_sends_to_it()
    {
        var calls = new List<string>();
        var handler = new FakeHandler(r =>
        {
            calls.Add(r.RequestUri!.AbsolutePath);
            return Json(HttpStatusCode.OK, "{\"ok\":true,\"result\":{}}");
        });

        var result = await Telegram(handler).TestAsync(TelegramIntegration(), new IntegrationTestOptions(" -100123 ", null), CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal([$"/bot{BotToken}/getMe", $"/bot{BotToken}/sendMessage"], calls);
        Assert.Contains("\"chat_id\":\"-100123\"", handler.LastBody);
    }

    [Fact]
    public async Task Telegram_test_names_a_chat_failure_as_an_integration_failure()
    {
        var handler = new FakeHandler(r => r.RequestUri!.AbsolutePath.EndsWith("getMe")
            ? Json(HttpStatusCode.OK, "{\"ok\":true,\"result\":{}}")
            : Json(HttpStatusCode.BadRequest, "{\"ok\":false,\"description\":\"Bad Request: chat not found\"}"));

        var result = await Telegram(handler).TestAsync(TelegramIntegration(), new IntegrationTestOptions("42", null), CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal("INTEGRATION_HTTP_ERROR", result.Code);
        Assert.Contains("chat not found", result.Message);
    }

    [Fact]
    public async Task Telegram_test_names_an_unreachable_chat_send_as_unreachable()
    {
        // Every test failure is named the same for every type; the step's NETWORK_ERROR is not one of them.
        var handler = new FakeHandler(r => r.RequestUri!.AbsolutePath.EndsWith("getMe")
            ? Json(HttpStatusCode.OK, "{\"ok\":true,\"result\":{}}")
            : throw new HttpRequestException("connection reset"));

        var result = await Telegram(handler).TestAsync(TelegramIntegration(), new IntegrationTestOptions("42", null), CancellationToken.None);

        Assert.Equal("INTEGRATION_UNREACHABLE", result.Code);
        Assert.True(result.Retryable);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "INTEGRATION_AUTH_FAILED", false)]
    [InlineData(HttpStatusCode.Unauthorized, "INTEGRATION_AUTH_FAILED", false)]
    [InlineData(HttpStatusCode.BadGateway, "INTEGRATION_HTTP_ERROR", true)]
    public async Task Telegram_test_names_a_refused_token(HttpStatusCode status, string code, bool retryable)
    {
        var handler = new FakeHandler(_ => Json(status, "{}"));

        var result = await Telegram(handler).TestAsync(TelegramIntegration(), null, CancellationToken.None);

        Assert.Equal(code, result.Code);
        Assert.Equal(retryable, result.Retryable);
        Assert.Equal((int)status, result.Status);
    }

    [Fact]
    public async Task Telegram_test_of_an_unreachable_api_is_unreachable_without_the_url()
    {
        var handler = new FakeHandler(_ => throw new HttpRequestException($"connect to https://tg.test/bot{BotToken}/getMe failed"));

        var result = await Telegram(handler).TestAsync(TelegramIntegration(), null, CancellationToken.None);

        Assert.Equal("INTEGRATION_UNREACHABLE", result.Code);
        Assert.DoesNotContain(BotToken, result.Message);
    }

    [Fact]
    public async Task Telegram_test_tolerates_an_answer_it_cannot_read()
    {
        var handler = new FakeHandler(_ => Json(HttpStatusCode.OK, "not json"));

        var result = await Telegram(handler).TestAsync(TelegramIntegration(), null, CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Null(result.Metadata);
    }

    [Fact]
    public async Task Telegram_sends_the_steps_options()
    {
        var handler = new FakeHandler(_ => Json(HttpStatusCode.OK, "{\"ok\":true,\"result\":{\"message_id\":5,\"chat\":{\"id\":-100}}}"));
        var config = new TelegramConfig { ChatId = "-100", Message = "*low*", ParseMode = "MarkdownV2", Silent = true, MessageThreadId = "12", DisableLinkPreview = true };

        var result = await Telegram(handler).SendAsync(TelegramIntegration(), config, CancellationToken.None);

        Assert.True(result.Ok);
        using var body = JsonDocument.Parse(handler.LastBody);
        Assert.Equal("MarkdownV2", body.RootElement.GetProperty("parse_mode").GetString());
        Assert.True(body.RootElement.GetProperty("disable_notification").GetBoolean());
        Assert.Equal(12, body.RootElement.GetProperty("message_thread_id").GetInt32());
        Assert.True(body.RootElement.GetProperty("link_preview_options").GetProperty("is_disabled").GetBoolean());
    }

    [Fact]
    public async Task Telegram_sends_plain_text_with_no_options_set()
    {
        var handler = new FakeHandler(_ => Json(HttpStatusCode.OK, "{\"ok\":true,\"result\":{}}"));

        await Telegram(handler).SendAsync(TelegramIntegration(), new TelegramConfig { ChatId = "@alerts", Message = "m" }, CancellationToken.None);

        using var body = JsonDocument.Parse(handler.LastBody);
        Assert.Equal(["chat_id", "text"], body.RootElement.EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public async Task Telegram_names_a_403_as_the_chat_refusing_the_bot()
    {
        var handler = new FakeHandler(_ => Json(HttpStatusCode.Forbidden, "{\"ok\":false,\"description\":\"Forbidden: bot was blocked by the user\"}"));

        var result = await Telegram(handler).SendAsync(TelegramIntegration(), new TelegramConfig { ChatId = "42", Message = "m" }, CancellationToken.None);

        Assert.Equal("TELEGRAM_CHAT_FORBIDDEN", result.Code);
        Assert.False(result.Retryable);
        Assert.Contains("bot was blocked by the user", result.Message);
    }

    [Fact]
    public async Task Telegram_test_names_a_forbidden_chat_as_an_integration_failure()
    {
        var handler = new FakeHandler(r => r.RequestUri!.AbsolutePath.EndsWith("getMe")
            ? Json(HttpStatusCode.OK, "{\"ok\":true,\"result\":{}}")
            : Json(HttpStatusCode.Forbidden, "{\"ok\":false,\"description\":\"Forbidden: bot was kicked from the group chat\"}"));

        var result = await Telegram(handler).TestAsync(TelegramIntegration(), new IntegrationTestOptions("-42", null), CancellationToken.None);

        Assert.Equal("INTEGRATION_CHAT_FORBIDDEN", result.Code);
    }

    [Fact]
    public async Task Telegram_lists_the_chats_it_heard_from_most_recent_first_once_each()
    {
        const string updates = """
            {"ok":true,"result":[
              {"update_id":1,"message":{"message_id":1,"chat":{"id":42,"type":"private","first_name":"Asha","last_name":"K","username":"asha"}}},
              {"update_id":2,"channel_post":{"message_id":2,"chat":{"id":-1001,"type":"channel","title":"Alerts","username":"alerts"}}},
              {"update_id":3,"my_chat_member":{"chat":{"id":-55,"type":"group","title":"Plant floor"}}},
              {"update_id":4,"callback_query":{"id":"x","message":{"message_id":3,"chat":{"id":42,"type":"private","first_name":"Asha"}}}},
              {"update_id":5,"poll":{"id":"p"}}
            ]}
            """;
        var handler = new FakeHandler(_ => Json(HttpStatusCode.OK, updates));

        var (result, chats) = await Telegram(handler).GetChatsAsync(TelegramIntegration(), CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal(HttpMethod.Get, handler.LastRequest!.Method);
        Assert.Equal($"https://tg.test/bot{BotToken}/getUpdates", handler.LastUri);
        Assert.Equal(["42", "-55", "-1001"], chats.Select(c => c.Id));
        Assert.Equal(new TelegramChat("-55", "group", "Plant floor", null), chats[1]);
        Assert.Equal(new TelegramChat("-1001", "channel", "Alerts", "alerts"), chats[2]);
        Assert.Equal("Asha", chats[0].Title);
    }

    [Fact]
    public async Task Telegram_chats_explains_a_bot_with_a_webhook()
    {
        var handler = new FakeHandler(_ => Json(HttpStatusCode.Conflict, "{\"ok\":false,\"description\":\"Conflict: can't use getUpdates method while webhook is active\"}"));

        var (result, chats) = await Telegram(handler).GetChatsAsync(TelegramIntegration(), CancellationToken.None);

        Assert.Equal("INTEGRATION_WEBHOOK_ACTIVE", result.Code);
        Assert.Empty(chats);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "INTEGRATION_AUTH_FAILED")]
    [InlineData(HttpStatusCode.NotFound, "INTEGRATION_AUTH_FAILED")]
    [InlineData(HttpStatusCode.BadGateway, "INTEGRATION_HTTP_ERROR")]
    public async Task Telegram_chats_names_a_refusal(HttpStatusCode status, string code)
    {
        var handler = new FakeHandler(_ => Json(status, "{}"));

        var (result, _) = await Telegram(handler).GetChatsAsync(TelegramIntegration(), CancellationToken.None);

        Assert.Equal(code, result.Code);
    }

    // -------------------------------------------------------------- discord

    [Fact]
    public async Task Discord_posts_the_message_with_mentions_disabled_and_waits_for_it()
    {
        var handler = new FakeHandler(_ => Json(HttpStatusCode.OK, "{\"id\":\"1300000000000000001\",\"channel_id\":\"555\"}"));

        var result = await new DiscordChannel(new SingleClientFactory(handler)).SendAsync(DiscordIntegration(), new DiscordConfig { Message = "@everyone tank low", Username = "Pump bot" }, CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal(200, result.Status);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal(DiscordWebhook + "?wait=true", handler.LastUri);
        using var body = JsonDocument.Parse(handler.LastBody);
        Assert.Equal("@everyone tank low", body.RootElement.GetProperty("content").GetString());
        Assert.Equal("Pump bot", body.RootElement.GetProperty("username").GetString());
        Assert.Equal(0, body.RootElement.GetProperty("allowed_mentions").GetProperty("parse").GetArrayLength());
        Assert.False(body.RootElement.TryGetProperty("embeds", out _));
        Assert.Equal("1300000000000000001", result.Metadata!.Value.GetProperty("messageId").GetString());
        Assert.Equal("555", result.Metadata.Value.GetProperty("channelId").GetString());
    }

    [Fact]
    public async Task Discord_leaves_out_a_blank_username()
    {
        var handler = new FakeHandler(_ => Json(HttpStatusCode.OK, "{}"));

        await new DiscordChannel(new SingleClientFactory(handler)).SendAsync(DiscordIntegration(), new DiscordConfig { Message = "hi", Username = " " }, CancellationToken.None);

        using var body = JsonDocument.Parse(handler.LastBody);
        Assert.False(body.RootElement.TryGetProperty("username", out _));
    }

    [Fact]
    public async Task Discord_posts_into_the_thread_with_the_avatar()
    {
        var handler = new FakeHandler(_ => Json(HttpStatusCode.OK, "{}"));

        await new DiscordChannel(new SingleClientFactory(handler)).SendAsync(
            DiscordIntegration(),
            new DiscordConfig { Message = "hi", ThreadId = "1200000000000000002", AvatarUrl = "https://example.test/pump.png" },
            CancellationToken.None);

        Assert.Equal(DiscordWebhook + "?wait=true&thread_id=1200000000000000002", handler.LastUri);
        using var body = JsonDocument.Parse(handler.LastBody);
        Assert.Equal("https://example.test/pump.png", body.RootElement.GetProperty("avatar_url").GetString());
    }

    [Fact]
    public async Task Discord_sends_embeds_in_discords_shape()
    {
        var handler = new FakeHandler(_ => Json(HttpStatusCode.OK, "{}"));
        var now = new DateTimeOffset(2026, 10, 30, 14, 32, 0, TimeSpan.Zero);
        var config = new DiscordConfig
        {
            Embeds =
            [
                new DiscordEmbed
                {
                    Title = "Freezer 2",
                    Url = "https://example.test/freezer",
                    Description = "Above -10 °C for 20 min",
                    Color = "#E03C31",
                    Fields = [new DiscordEmbedField { Name = "Temp", Value = "-8 °C", Inline = true }],
                    Footer = "Wbskt",
                    ImageUrl = "https://example.test/chart.png",
                    ThumbnailUrl = "https://example.test/icon.png",
                    Timestamp = true
                }
            ]
        };

        await new DiscordChannel(new SingleClientFactory(handler), new FixedClock(now)).SendAsync(DiscordIntegration(), config, CancellationToken.None);

        using var body = JsonDocument.Parse(handler.LastBody);
        Assert.False(body.RootElement.TryGetProperty("content", out _));
        var embed = body.RootElement.GetProperty("embeds")[0];
        Assert.Equal("Freezer 2", embed.GetProperty("title").GetString());
        Assert.Equal("https://example.test/freezer", embed.GetProperty("url").GetString());
        Assert.Equal(0xE03C31, embed.GetProperty("color").GetInt32());
        Assert.Equal("Temp", embed.GetProperty("fields")[0].GetProperty("name").GetString());
        Assert.True(embed.GetProperty("fields")[0].GetProperty("inline").GetBoolean());
        Assert.Equal("Wbskt", embed.GetProperty("footer").GetProperty("text").GetString());
        Assert.Equal("https://example.test/chart.png", embed.GetProperty("image").GetProperty("url").GetString());
        Assert.Equal("https://example.test/icon.png", embed.GetProperty("thumbnail").GetProperty("url").GetString());
        Assert.Equal(now, embed.GetProperty("timestamp").GetDateTimeOffset());
    }

    [Fact]
    public async Task Discord_send_that_answers_without_a_message_still_succeeds()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        var result = await new DiscordChannel(new SingleClientFactory(handler)).SendAsync(DiscordIntegration(), new DiscordConfig { Message = "hi" }, CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Null(result.Metadata);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "DISCORD_HTTP_ERROR", false)]
    [InlineData(HttpStatusCode.Unauthorized, "DISCORD_AUTH_FAILED", false)]
    [InlineData((HttpStatusCode)429, "DISCORD_HTTP_ERROR", true)]
    [InlineData(HttpStatusCode.InternalServerError, "DISCORD_HTTP_ERROR", true)]
    public async Task Discord_send_failures_are_named_for_the_step(HttpStatusCode status, string code, bool retryable)
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(status));

        var result = await new DiscordChannel(new SingleClientFactory(handler)).SendAsync(DiscordIntegration(), new DiscordConfig { Message = "hi" }, CancellationToken.None);

        Assert.Equal(code, result.Code);
        Assert.Equal(retryable, result.Retryable);
    }

    [Fact]
    public async Task Discord_test_reads_the_webhook_without_posting()
    {
        var handler = new FakeHandler(_ => Json(HttpStatusCode.OK, "{\"name\":\"Alerts\",\"channel_id\":\"555\"}"));

        var result = await new DiscordChannel(new SingleClientFactory(handler)).TestAsync(DiscordIntegration(), null, CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal(HttpMethod.Get, handler.LastRequest!.Method);
        Assert.Equal("Alerts", result.Metadata!.Value.GetProperty("webhookName").GetString());
        Assert.Equal("555", result.Metadata.Value.GetProperty("channelId").GetString());
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task Discord_test_of_a_deleted_webhook_is_an_auth_failure(HttpStatusCode status)
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(status));

        var result = await new DiscordChannel(new SingleClientFactory(handler)).TestAsync(DiscordIntegration(), null, CancellationToken.None);

        Assert.Equal("INTEGRATION_AUTH_FAILED", result.Code);
    }

    // ----------------------------------------------------------------- ntfy

    [Fact]
    public async Task Ntfy_publishes_json_to_the_server_root_with_the_access_token()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));

        var result = await Ntfy(handler).PublishAsync(NtfyIntegration("tk_abcdefghijkl"), "Tank low", "Pump", 4, ["warning", "droplet"], CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal("https://ntfy.example.test/", handler.LastUri);
        Assert.Equal("Bearer", handler.LastRequest!.Headers.Authorization!.Scheme);
        Assert.Equal("tk_abcdefghijkl", handler.LastRequest.Headers.Authorization.Parameter);
        Assert.Equal("application/json", handler.LastRequest.Content!.Headers.ContentType!.MediaType);
        using var body = JsonDocument.Parse(handler.LastBody);
        Assert.Equal("alerts", body.RootElement.GetProperty("topic").GetString());
        Assert.Equal("Tank low", body.RootElement.GetProperty("message").GetString());
        Assert.Equal("Pump", body.RootElement.GetProperty("title").GetString());
        Assert.Equal(4, body.RootElement.GetProperty("priority").GetInt32());
        Assert.Equal(["warning", "droplet"], body.RootElement.GetProperty("tags").EnumerateArray().Select(t => t.GetString()));
    }

    [Fact]
    public async Task Ntfy_leaves_out_what_the_step_does_not_set()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));

        await Ntfy(handler).PublishAsync(NtfyIntegration(null), "Tank low", " ", null, [], CancellationToken.None);

        Assert.Null(handler.LastRequest!.Headers.Authorization);
        using var body = JsonDocument.Parse(handler.LastBody);
        Assert.Equal(["topic", "message"], body.RootElement.EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public async Task Ntfy_never_calls_a_blocked_server()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var channel = new NtfyChannel(new SingleClientFactory(handler), new BlockingGuard());

        var result = await channel.PublishAsync(NtfyIntegration(null), "m", null, null, null, CancellationToken.None);

        Assert.Equal("NTFY_BLOCKED_TARGET", result.Code);
        Assert.False(result.Retryable);
        Assert.Null(handler.LastUri);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "INTEGRATION_AUTH_FAILED")]
    [InlineData(HttpStatusCode.BadRequest, "INTEGRATION_HTTP_ERROR")]
    public async Task Ntfy_test_failures_are_named_as_integration_failures(HttpStatusCode status, string code)
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(status));

        var result = await Ntfy(handler).TestAsync(NtfyIntegration(null), null, CancellationToken.None);

        Assert.Equal(code, result.Code);
    }

    [Fact]
    public async Task Ntfy_test_of_an_unreachable_server_is_unreachable()
    {
        var handler = new FakeHandler(_ => throw new HttpRequestException("refused"));

        var result = await Ntfy(handler).TestAsync(NtfyIntegration(null), null, CancellationToken.None);

        Assert.Equal("INTEGRATION_UNREACHABLE", result.Code);
        Assert.True(result.Retryable);
    }

    // ------------------------------------------------------------ http auth

    [Fact]
    public void Authorize_puts_a_bearer_token_in_authorization()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.test/");

        HttpAuthChannel.Authorize(request, HttpAuthIntegration(HttpAuthSettings.Bearer, new IntegrationSecrets { Token = "tok-1" }));

        Assert.Equal("Bearer tok-1", request.Headers.Authorization!.ToString());
    }

    [Fact]
    public void Authorize_puts_basic_credentials_in_authorization()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.test/");

        HttpAuthChannel.Authorize(request, HttpAuthIntegration(HttpAuthSettings.Basic, new IntegrationSecrets { Password = "pä:ss" }));

        Assert.Equal("Basic", request.Headers.Authorization!.Scheme);
        Assert.Equal("svc:pä:ss", Encoding.UTF8.GetString(Convert.FromBase64String(request.Headers.Authorization.Parameter!)));
    }

    [Fact]
    public void Authorize_puts_a_header_value_in_its_own_header_replacing_any_there()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.test/");
        request.Headers.TryAddWithoutValidation("x-api-key", "from-the-step");

        HttpAuthChannel.Authorize(request, HttpAuthIntegration(HttpAuthSettings.Header, new IntegrationSecrets { HeaderValue = "key-1" }));

        Assert.Equal(["key-1"], request.Headers.GetValues("X-Api-Key"));
        Assert.Null(request.Headers.Authorization);
    }

    [Theory]
    [InlineData("api/states", "https://ha.example.test:8123/base/api/states")]
    [InlineData("/api/states", "https://ha.example.test:8123/base/api/states")]
    [InlineData("//evil.test/x", "https://ha.example.test:8123/base/evil.test/x")]
    [InlineData("api/states?x=1", "https://ha.example.test:8123/base/api/states?x=1")]
    [InlineData("https://ha.example.test:8123/other", "https://ha.example.test:8123/other")]
    [InlineData("HTTPS://HA.EXAMPLE.TEST:8123/other", "https://ha.example.test:8123/other")]
    public void Target_keeps_the_request_on_the_base_urls_origin(string url, string expected)
    {
        var settings = new HttpAuthSettings { BaseUrl = "https://ha.example.test:8123/base", Mode = HttpAuthSettings.Bearer };

        Assert.Equal(expected, HttpAuthChannel.Target(settings, url)?.ToString());
    }

    [Theory]
    [InlineData("https://evil.test/api")]
    [InlineData("http://ha.example.test:8123/api")]
    [InlineData("https://ha.example.test/api")]
    [InlineData("https://ha.example.test:8124/api")]
    [InlineData("https://sub.ha.example.test:8123/api")]
    public void Target_refuses_another_origin(string url)
    {
        var settings = new HttpAuthSettings { BaseUrl = "https://ha.example.test:8123", Mode = HttpAuthSettings.Bearer };

        Assert.Null(HttpAuthChannel.Target(settings, url));
    }

    [Fact]
    public async Task Home_assistant_test_reads_its_config_with_the_token()
    {
        var handler = new FakeHandler(_ => Json(HttpStatusCode.OK, "{\"version\":\"2026.10.1\",\"location_name\":\"Home\"}"));
        var channel = new HttpAuthChannel(new SingleClientFactory(handler), new AllowAllGuard());
        var integration = HttpAuthIntegration(HttpAuthSettings.Bearer, new IntegrationSecrets { Token = "llat" }, IntegrationPreset.HomeAssistant);

        var result = await channel.TestAsync(integration, null, CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal("https://api.example.test/api/config", handler.LastUri);
        Assert.Equal("Bearer llat", handler.LastRequest!.Headers.Authorization!.ToString());
        Assert.Equal("2026.10.1", result.Metadata!.Value.GetProperty("version").GetString());
        Assert.Equal("Home", result.Metadata.Value.GetProperty("locationName").GetString());
    }

    [Fact]
    public async Task Http_auth_test_without_a_preset_gets_the_base_url_and_reports_nothing_about_it()
    {
        var handler = new FakeHandler(_ => Json(HttpStatusCode.OK, "{\"version\":\"1\"}"));
        var channel = new HttpAuthChannel(new SingleClientFactory(handler), new AllowAllGuard());

        var result = await channel.TestAsync(HttpAuthIntegration(HttpAuthSettings.Header, new IntegrationSecrets { HeaderValue = "key-1" }), null, CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal("https://api.example.test/", handler.LastUri);
        Assert.Equal(["key-1"], handler.LastRequest!.Headers.GetValues("X-Api-Key"));
        Assert.Null(result.Metadata);
    }

    [Fact]
    public async Task Http_auth_test_of_a_refused_credential_is_an_auth_failure()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var channel = new HttpAuthChannel(new SingleClientFactory(handler), new AllowAllGuard());

        var result = await channel.TestAsync(HttpAuthIntegration(HttpAuthSettings.Bearer, new IntegrationSecrets { Token = "t" }), null, CancellationToken.None);

        Assert.Equal("INTEGRATION_AUTH_FAILED", result.Code);
        Assert.Equal(401, result.Status);
    }

    [Fact]
    public async Task Http_auth_test_never_sends_the_credential_to_a_blocked_address()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var channel = new HttpAuthChannel(new SingleClientFactory(handler), new BlockingGuard());

        var result = await channel.TestAsync(HttpAuthIntegration(HttpAuthSettings.Bearer, new IntegrationSecrets { Token = "t" }), null, CancellationToken.None);

        Assert.Equal("INTEGRATION_BLOCKED_TARGET", result.Code);
        Assert.Null(handler.LastUri);
    }

    // ---------------------------------------------------------------- shared

    [Fact]
    public void Scrub_blanks_every_secret_and_caps_the_length()
    {
        var secrets = new IntegrationSecrets { Topic = "alerts", AccessToken = "tk_abcdefghijkl", Token = "abc" };

        Assert.Equal("•••• at ••••; abc", IntegrationErrors.Scrub("tk_abcdefghijkl at alerts; abc", secrets));
        Assert.Equal(IntegrationErrors.MaxLength, IntegrationErrors.Scrub(new string('x', 1000), secrets).Length);
        Assert.Equal(string.Empty, IntegrationErrors.Scrub(null, secrets));
    }

    // ----------------------------------------------------------------- glue

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static TelegramChannel Telegram(HttpMessageHandler handler) =>
        new(new SingleClientFactory(handler), Microsoft.Extensions.Options.Options.Create(new IntegrationOptions { TelegramApiBaseUrl = "https://tg.test/" }));

    private static NtfyChannel Ntfy(HttpMessageHandler handler) => new(new SingleClientFactory(handler), new AllowAllGuard());

    private static ResolvedIntegration TelegramIntegration() =>
        new(1, Guid.NewGuid(), 9, IntegrationType.Telegram, null, new TelegramSettings(), new IntegrationSecrets { BotToken = BotToken });

    private static ResolvedIntegration DiscordIntegration() =>
        new(2, Guid.NewGuid(), 9, IntegrationType.Discord, null, new DiscordSettings(), new IntegrationSecrets { WebhookUrl = DiscordWebhook });

    private static ResolvedIntegration NtfyIntegration(string? accessToken) =>
        new(3, Guid.NewGuid(), 9, IntegrationType.Ntfy, null, new NtfySettings { ServerUrl = "https://ntfy.example.test" }, new IntegrationSecrets { Topic = "alerts", AccessToken = accessToken });

    private static ResolvedIntegration HttpAuthIntegration(string mode, IntegrationSecrets secrets, IntegrationPreset? preset = null) =>
        new(4, Guid.NewGuid(), 9, IntegrationType.HttpAuth, preset,
            new HttpAuthSettings { BaseUrl = "https://api.example.test", Mode = mode, UserName = mode == HttpAuthSettings.Basic ? "svc" : null, HeaderName = mode == HttpAuthSettings.Header ? "X-Api-Key" : null },
            secrets);

    private sealed class BlockingGuard : IOutboundAddressGuard
    {
        public ValueTask<OutboundAddressDecision> EvaluateAsync(Uri uri, CancellationToken ct) => ValueTask.FromResult(OutboundAddressDecision.Block("private address"));
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
