using System.Text.Json;
using Wbskt.Workflow.Abstraction.Integrations;
using Wbskt.Workflow.Integrations;
using static Wbskt.Host.Tests.Integrations.IntegrationTestKit;

namespace Wbskt.Host.Tests.Integrations;

public sealed class IntegrationShapeTests
{
    private static readonly IntegrationOptions Production = new();
    private static readonly IntegrationOptions Development = new() { AllowInsecureTransport = true };

    // ------------------------------------------------------------- settings

    [Theory]
    [InlineData(IntegrationType.Telegram)]
    [InlineData(IntegrationType.Discord)]
    public void A_type_with_no_settings_takes_an_empty_or_missing_config(IntegrationType type)
    {
        Assert.True(IntegrationShape.ParseSettings(type, null, null, Production).IsSuccess);
        Assert.True(IntegrationShape.ParseSettings(type, null, Raw("{}"), Production).IsSuccess);
        Assert.True(IntegrationShape.ParseSettings(type, null, Raw("null"), Production).IsSuccess);
    }

    [Theory]
    [InlineData(IntegrationType.Telegram, "{\"chatId\":\"1\"}")]
    [InlineData(IntegrationType.Smtp, "{\"host\":\"smtp.example.test\",\"fromAddress\":\"a@example.test\",\"hostname\":\"x\"}")]
    [InlineData(IntegrationType.Ntfy, "{\"server\":\"https://ntfy.example.test\"}")]
    public void A_misspelt_setting_is_refused_rather_than_dropped(IntegrationType type, string config)
    {
        var result = IntegrationShape.ParseSettings(type, null, Raw(config), Production);

        Assert.Equal(IntegrationShape.ConfigInvalid, result.Error.Code);
    }

    [Fact]
    public void Config_must_be_an_object()
    {
        var result = IntegrationShape.ParseSettings(IntegrationType.Telegram, null, Raw("[]"), Production);

        Assert.Equal(IntegrationShape.ConfigInvalid, result.Error.Code);
    }

    [Fact]
    public void A_preset_of_another_type_is_refused()
    {
        var result = IntegrationShape.ParseSettings(IntegrationType.Telegram, IntegrationPreset.HomeAssistant, null, Production);

        Assert.Equal(IntegrationShape.PresetInvalid, result.Error.Code);
    }

    [Fact]
    public void Smtp_defaults_to_587_with_starttls()
    {
        var smtp = Smtp(new { host = " smtp.example.test ", fromAddress = "alerts@example.test" });

        Assert.Equal("smtp.example.test", smtp.Host);
        Assert.Equal(587, smtp.Port);
        Assert.Equal(SmtpSettings.StartTls, smtp.Tls);
        Assert.Null(smtp.UserName);
        Assert.Null(smtp.FromName);
    }

    [Fact]
    public void Smtp_on_465_defaults_to_implicit_tls()
    {
        Assert.Equal(SmtpSettings.Implicit, Smtp(new { host = "smtp.example.test", port = 465, fromAddress = "a@example.test" }).Tls);
    }

    [Fact]
    public void Smtp_keeps_only_the_address_of_a_named_from_and_trims_the_rest()
    {
        var smtp = Smtp(new { host = "smtp.example.test", fromAddress = "Alerts <alerts@example.test>", fromName = " Wbskt ", userName = "  " });

        Assert.Equal("alerts@example.test", smtp.FromAddress);
        Assert.Equal("Wbskt", smtp.FromName);
        Assert.Null(smtp.UserName);
    }

    [Theory]
    [InlineData("{\"fromAddress\":\"a@example.test\"}", "host")]
    [InlineData("{\"host\":\"bad host!\",\"fromAddress\":\"a@example.test\"}", "host")]
    [InlineData("{\"host\":\"smtp.example.test\",\"port\":0,\"fromAddress\":\"a@example.test\"}", "port")]
    [InlineData("{\"host\":\"smtp.example.test\",\"port\":2526,\"fromAddress\":\"a@example.test\"}", "port must be one of")]
    [InlineData("{\"host\":\"smtp.example.test\",\"tls\":\"ssl\",\"fromAddress\":\"a@example.test\"}", "tls")]
    [InlineData("{\"host\":\"smtp.example.test\"}", "fromAddress")]
    [InlineData("{\"host\":\"smtp.example.test\",\"fromAddress\":\"not an address\"}", "fromAddress")]
    [InlineData("{\"host\":\"smtp.example.test\",\"fromAddress\":\"a@example.test\",\"fromName\":\"two\\nlines\"}", "fromName")]
    public void Smtp_refuses_bad_settings(string config, string mentions)
    {
        var result = IntegrationShape.ParseSettings(IntegrationType.Smtp, null, Raw(config), Production);

        Assert.Equal(IntegrationShape.ConfigInvalid, result.Error.Code);
        Assert.Contains(mentions, result.Error.Message);
    }

    [Fact]
    public void Smtp_allows_any_port_in_development_only()
    {
        var config = Raw("{\"host\":\"localhost\",\"port\":2526,\"fromAddress\":\"a@example.test\"}");

        Assert.True(IntegrationShape.ParseSettings(IntegrationType.Smtp, null, config, Development).IsSuccess);
        Assert.True(IntegrationShape.ParseSettings(IntegrationType.Smtp, null, config, new IntegrationOptions { SmtpPorts = [2526] }).IsSuccess);
    }

    [Fact]
    public void Ntfy_defaults_to_ntfy_sh()
    {
        var ntfy = (NtfySettings)IntegrationShape.ParseSettings(IntegrationType.Ntfy, null, null, Production).Value;

        Assert.Equal("https://ntfy.sh", ntfy.ServerUrl);
    }

    [Fact]
    public void Ntfy_drops_a_trailing_slash_from_the_server()
    {
        var ntfy = (NtfySettings)IntegrationShape.ParseSettings(IntegrationType.Ntfy, null, ToJson(new { serverUrl = "https://ntfy.example.test/" }), Production).Value;

        Assert.Equal("https://ntfy.example.test", ntfy.ServerUrl);
    }

    [Theory]
    [InlineData("http://ntfy.example.test")]
    [InlineData("https://user:pw@ntfy.example.test")]
    [InlineData("https://ntfy.example.test/?x=1")]
    [InlineData("https://ntfy.example.test/#top")]
    [InlineData("ntfy.example.test")]
    public void Ntfy_refuses_a_server_that_is_not_a_plain_https_url(string serverUrl)
    {
        var result = IntegrationShape.ParseSettings(IntegrationType.Ntfy, null, ToJson(new { serverUrl }), Production);

        Assert.Equal(IntegrationShape.ConfigInvalid, result.Error.Code);
    }

    [Fact]
    public void Ntfy_allows_plain_http_in_development()
    {
        Assert.True(IntegrationShape.ParseSettings(IntegrationType.Ntfy, null, ToJson(new { serverUrl = "http://localhost:8080" }), Development).IsSuccess);
    }

    [Fact]
    public void Home_assistant_defaults_to_bearer()
    {
        var http = HttpAuth(new { baseUrl = "https://ha.example.test:8123/" }, IntegrationPreset.HomeAssistant);

        Assert.Equal(HttpAuthSettings.Bearer, http.Mode);
        Assert.Equal("https://ha.example.test:8123", http.BaseUrl);
        Assert.Equal("Authorization", http.AuthHeaderName);
    }

    [Fact]
    public void Home_assistant_refuses_any_other_mode()
    {
        var result = IntegrationShape.ParseSettings(
            IntegrationType.HttpAuth, IntegrationPreset.HomeAssistant, ToJson(new { baseUrl = "https://ha.example.test", mode = "basic", userName = "u" }), Production);

        Assert.Equal(IntegrationShape.ConfigInvalid, result.Error.Code);
        Assert.Contains("bearer", result.Error.Message);
    }

    [Fact]
    public void Http_auth_without_a_preset_needs_a_mode()
    {
        var result = IntegrationShape.ParseSettings(IntegrationType.HttpAuth, null, ToJson(new { baseUrl = "https://api.example.test" }), Production);

        Assert.Equal(IntegrationShape.ConfigInvalid, result.Error.Code);
        Assert.Contains("mode", result.Error.Message);
    }

    [Fact]
    public void Http_auth_keeps_only_what_its_mode_uses()
    {
        var basic = HttpAuth(new { baseUrl = "https://api.example.test", mode = "basic", userName = " svc ", headerName = "X-Api-Key" });
        var header = HttpAuth(new { baseUrl = "https://api.example.test", mode = "header", headerName = " X-Api-Key ", userName = "svc" });

        Assert.Equal("svc", basic.UserName);
        Assert.Null(basic.HeaderName);
        Assert.Equal("X-Api-Key", header.HeaderName);
        Assert.Null(header.UserName);
        Assert.Equal("X-Api-Key", header.AuthHeaderName);
    }

    [Theory]
    [InlineData("{\"mode\":\"bearer\"}", "baseUrl is required")]
    [InlineData("{\"baseUrl\":\"http://api.example.test\",\"mode\":\"bearer\"}", "https")]
    [InlineData("{\"baseUrl\":\"https://api.example.test\",\"mode\":\"basic\"}", "userName")]
    [InlineData("{\"baseUrl\":\"https://api.example.test\",\"mode\":\"basic\",\"userName\":\"a:b\"}", "userName")]
    [InlineData("{\"baseUrl\":\"https://api.example.test\",\"mode\":\"header\"}", "headerName")]
    [InlineData("{\"baseUrl\":\"https://api.example.test\",\"mode\":\"header\",\"headerName\":\"X Api\"}", "headerName")]
    [InlineData("{\"baseUrl\":\"https://api.example.test\",\"mode\":\"header\",\"headerName\":\"content-type\"}", "set by the request")]
    [InlineData("{\"baseUrl\":\"https://api.example.test\",\"mode\":\"header\",\"headerName\":\"Cookie\"}", "set by the request")]
    [InlineData("{\"baseUrl\":\"https://api.example.test\",\"mode\":\"oauth\"}", "mode must be")]
    public void Http_auth_refuses_bad_settings(string config, string mentions)
    {
        var result = IntegrationShape.ParseSettings(IntegrationType.HttpAuth, null, Raw(config), Production);

        Assert.Equal(IntegrationShape.ConfigInvalid, result.Error.Code);
        Assert.Contains(mentions, result.Error.Message);
    }

    // -------------------------------------------------------------- secrets

    [Fact]
    public void No_secrets_parse_as_empty()
    {
        Assert.Same(IntegrationSecrets.Empty, IntegrationShape.ParseSecrets(null).Value);
        Assert.Same(IntegrationSecrets.Empty, IntegrationShape.ParseSecrets(Raw("null")).Value);
    }

    [Fact]
    public void Secrets_must_be_an_object()
    {
        Assert.Equal(IntegrationShape.SecretInvalid, IntegrationShape.ParseSecrets(Raw("\"token\"")).Error.Code);
    }

    [Theory]
    [InlineData("{\"apiKey\":\"hunter2-very-secret\"}")]
    [InlineData("{\"botToken\":12345678901234}")]
    public void A_bad_secret_field_is_named_without_its_value(string secrets)
    {
        var result = IntegrationShape.ParseSecrets(Raw(secrets));

        Assert.Equal(IntegrationShape.SecretInvalid, result.Error.Code);
        Assert.DoesNotContain("hunter2", result.Error.Message);
        Assert.DoesNotContain("12345678901234", result.Error.Message);
    }

    [Fact]
    public void Merge_keeps_what_the_patch_leaves_out_replaces_what_it_sets_and_clears_on_empty()
    {
        var stored = new IntegrationSecrets { Topic = "alerts", AccessToken = "tk_old" };

        Assert.Equal(stored, IntegrationShape.Merge(stored, IntegrationSecrets.Empty));
        Assert.Equal(new IntegrationSecrets { Topic = "alerts", AccessToken = "tk_new" }, IntegrationShape.Merge(stored, new IntegrationSecrets { AccessToken = "tk_new" }));
        Assert.Equal(new IntegrationSecrets { Topic = "alerts" }, IntegrationShape.Merge(stored, new IntegrationSecrets { AccessToken = "" }));
    }

    [Fact]
    public void A_secret_field_the_type_does_not_have_is_refused()
    {
        var result = IntegrationShape.CheckFields(IntegrationType.Telegram, new IntegrationSecrets { BotToken = BotToken, WebhookUrl = "https://discord.com/api/webhooks/1/x" });

        Assert.Equal(IntegrationShape.SecretInvalid, result.Error.Code);
        Assert.Contains("'webhookUrl'", result.Error.Message);
    }

    [Fact]
    public void Every_field_a_type_has_passes_the_field_check()
    {
        Assert.True(IntegrationShape.CheckFields(IntegrationType.Ntfy, new IntegrationSecrets { Topic = "t", AccessToken = "a" }).IsSuccess);
        Assert.True(IntegrationShape.CheckFields(IntegrationType.HttpAuth, new IntegrationSecrets { Token = "t", Password = "p", HeaderValue = "h" }).IsSuccess);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-token")]
    [InlineData("123456:short")]
    public void Telegram_needs_a_bot_token_that_looks_like_one(string? botToken)
    {
        var result = Validate(IntegrationType.Telegram, new TelegramSettings(), new IntegrationSecrets { BotToken = botToken });

        Assert.Equal(IntegrationShape.SecretInvalid, result.Error.Code);
        Assert.Contains("botToken", result.Error.Message);
    }

    [Theory]
    [InlineData("https://discord.com/api/webhooks/1/abc")]
    [InlineData("https://canary.discord.com/api/webhooks/1/abc")]
    [InlineData("https://discordapp.com/api/webhooks/1/abc")]
    public void Discord_takes_a_webhook_on_discords_hosts(string webhookUrl)
    {
        Assert.True(Validate(IntegrationType.Discord, new DiscordSettings(), new IntegrationSecrets { WebhookUrl = webhookUrl }).IsSuccess);
    }

    [Theory]
    [InlineData("")]
    [InlineData("https://evil-discord.com/api/webhooks/1/abc")]
    [InlineData("https://discord.com.evil.test/api/webhooks/1/abc")]
    [InlineData("https://discord.com/api/other/1/abc")]
    [InlineData("http://discord.com/api/webhooks/1/abc")]
    [InlineData("https://user@discord.com/api/webhooks/1/abc")]
    [InlineData("https://discord.com/api/webhooks/1/abc#x")]
    [InlineData("discord.com/api/webhooks/1/abc")]
    public void Discord_refuses_anything_but_a_discord_webhook(string webhookUrl)
    {
        var result = Validate(IntegrationType.Discord, new DiscordSettings(), new IntegrationSecrets { WebhookUrl = webhookUrl });

        Assert.Equal(IntegrationShape.SecretInvalid, result.Error.Code);
    }

    [Fact]
    public void Discord_takes_configured_hosts_and_plain_http_in_development()
    {
        var options = new IntegrationOptions { AllowInsecureTransport = true, DiscordHosts = ["discord-fake"] };

        Assert.True(IntegrationShape.ValidateSecrets(IntegrationType.Discord, new DiscordSettings(), new IntegrationSecrets { WebhookUrl = "http://discord-fake:5000/api/webhooks/1/a" }, options).IsSuccess);
        Assert.False(IntegrationShape.ValidateSecrets(IntegrationType.Discord, new DiscordSettings(), new IntegrationSecrets { WebhookUrl = "https://discord.com/api/webhooks/1/a" }, options).IsSuccess);
    }

    [Fact]
    public void Smtp_needs_a_password_exactly_when_it_signs_in()
    {
        var anonymous = new SmtpSettings { Host = "h", Port = 587, Tls = SmtpSettings.StartTls, FromAddress = "a@b.test" };
        var signedIn = anonymous with { UserName = "svc" };

        Assert.True(Validate(IntegrationType.Smtp, anonymous, IntegrationSecrets.Empty).IsSuccess);
        Assert.True(Validate(IntegrationType.Smtp, signedIn, new IntegrationSecrets { Password = "pw" }).IsSuccess);
        Assert.Contains("required when userName", Validate(IntegrationType.Smtp, signedIn, IntegrationSecrets.Empty).Error.Message);
        Assert.Contains("needs a userName", Validate(IntegrationType.Smtp, anonymous, new IntegrationSecrets { Password = "pw" }).Error.Message);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("alerts_home-1", true)]
    [InlineData("has space", false)]
    [InlineData("a/b", false)]
    public void Ntfy_needs_a_plain_topic(string? topic, bool valid)
    {
        Assert.Equal(valid, Validate(IntegrationType.Ntfy, new NtfySettings { ServerUrl = "https://ntfy.sh" }, new IntegrationSecrets { Topic = topic }).IsSuccess);
    }

    [Fact]
    public void Ntfy_topic_is_at_most_64_characters()
    {
        var settings = new NtfySettings { ServerUrl = "https://ntfy.sh" };

        Assert.True(Validate(IntegrationType.Ntfy, settings, new IntegrationSecrets { Topic = new string('a', 64) }).IsSuccess);
        Assert.False(Validate(IntegrationType.Ntfy, settings, new IntegrationSecrets { Topic = new string('a', 65) }).IsSuccess);
    }

    [Theory]
    [InlineData(HttpAuthSettings.Bearer, "token")]
    [InlineData(HttpAuthSettings.Basic, "password")]
    [InlineData(HttpAuthSettings.Header, "headerValue")]
    public void Http_auth_keeps_only_the_secret_its_mode_uses(string mode, string field)
    {
        var settings = new HttpAuthSettings { BaseUrl = "https://api.example.test", Mode = mode, UserName = "u", HeaderName = "X-Key" };
        var all = new IntegrationSecrets { Token = "tok", Password = "pw", HeaderValue = "hv" };

        var kept = Validate(IntegrationType.HttpAuth, settings, all).Value;

        Assert.Equal([field], kept.Fields().Where(f => f.Value is not null).Select(f => f.Name));
    }

    [Fact]
    public void Http_auth_refuses_a_mode_without_its_secret()
    {
        // Switching from basic to bearer drops the password rather than sealing it unused.
        var settings = new HttpAuthSettings { BaseUrl = "https://api.example.test", Mode = HttpAuthSettings.Bearer };

        var result = Validate(IntegrationType.HttpAuth, settings, new IntegrationSecrets { Password = "pw" });

        Assert.Equal(IntegrationShape.SecretInvalid, result.Error.Code);
        Assert.Contains("token is required for bearer", result.Error.Message);
    }

    [Fact]
    public void A_secret_with_a_line_break_or_over_4096_characters_is_refused()
    {
        var settings = new NtfySettings { ServerUrl = "https://ntfy.sh" };

        Assert.False(Validate(IntegrationType.Ntfy, settings, new IntegrationSecrets { Topic = "t", AccessToken = "a\r\nX-Injected: 1" }).IsSuccess);
        Assert.False(Validate(IntegrationType.Ntfy, settings, new IntegrationSecrets { Topic = "t", AccessToken = new string('a', 4097) }).IsSuccess);
        Assert.True(Validate(IntegrationType.Ntfy, settings, new IntegrationSecrets { Topic = "t", AccessToken = new string('a', 4096) }).IsSuccess);
    }

    [Fact]
    public void Settings_of_another_type_are_refused()
    {
        var result = Validate(IntegrationType.HttpAuth, new NtfySettings(), new IntegrationSecrets { Token = "t" });

        Assert.Equal(IntegrationShape.ConfigInvalid, result.Error.Code);
    }

    // ----------------------------------------------------------------- hint

    [Fact]
    public void A_hint_shows_the_last_four_characters_of_a_long_secret()
    {
        Assert.Equal("••••0123", IntegrationShape.Hint(IntegrationType.Telegram, new IntegrationSecrets { BotToken = BotToken }));
        Assert.Equal("••••9012", IntegrationShape.Hint(IntegrationType.Ntfy, new IntegrationSecrets { Topic = "abcdefgh9012", AccessToken = "zzzzzzzzzzzzzzz" }));
    }

    [Theory]
    [InlineData("a")]
    [InlineData("abcdefghijk")]
    public void A_hint_shows_nothing_of_a_short_secret(string password)
    {
        Assert.Equal("••••", IntegrationShape.Hint(IntegrationType.Smtp, new IntegrationSecrets { Password = password }));
    }

    [Fact]
    public void There_is_no_hint_without_a_secret()
    {
        Assert.Null(IntegrationShape.Hint(IntegrationType.Smtp, IntegrationSecrets.Empty));
    }

    [Theory]
    [InlineData("token-abcd1234", null, null, "••••1234")]
    [InlineData(null, "password-wxyz", null, "••••wxyz")]
    [InlineData(null, null, "header-value-9876", "••••9876")]
    public void An_http_auth_hint_is_of_whichever_credential_it_has(string? token, string? password, string? headerValue, string expected)
    {
        Assert.Equal(expected, IntegrationShape.Hint(IntegrationType.HttpAuth, new IntegrationSecrets { Token = token, Password = password, HeaderValue = headerValue }));
    }

    // ----------------------------------------------------------------- glue

    private static SmtpSettings Smtp(object config)
    {
        var result = IntegrationShape.ParseSettings(IntegrationType.Smtp, null, ToJson(config), Production);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        return (SmtpSettings)result.Value;
    }

    private static HttpAuthSettings HttpAuth(object config, IntegrationPreset? preset = null)
    {
        var result = IntegrationShape.ParseSettings(IntegrationType.HttpAuth, preset, ToJson(config), Production);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        return (HttpAuthSettings)result.Value;
    }

    private static Wbskt.Infrastructure.Result<IntegrationSecrets> Validate(IntegrationType type, IntegrationSettings settings, IntegrationSecrets secrets) =>
        IntegrationShape.ValidateSecrets(type, settings, secrets, Production);
}
