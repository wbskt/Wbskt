using System.Text.Json;
using Microsoft.Extensions.Options;
using Wbskt.Workflow.Abstraction.Integrations;
using Wbskt.Workflow.Engine.Contracts;
using Wbskt.Workflow.Integrations;
using static Wbskt.Host.Tests.Integrations.IntegrationTestKit;

namespace Wbskt.Host.Tests.Integrations;

public sealed class IntegrationVaultTests
{
    private const int WorkspaceId = 4;
    private static readonly Guid RefId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    [Fact]
    public void Sealing_a_new_integration_fills_in_defaults_and_marks_the_secret_changed()
    {
        var vault = Vault();

        var response = vault.Seal(Request("smtp", ToJson(new { host = "smtp.example.test", fromAddress = "a@example.test", userName = "svc" }), ToJson(new { password = "correct-horse-battery" }))).Value;

        Assert.True(response.SecretChanged);
        Assert.Equal(587, response.Config.GetProperty("port").GetInt32());
        Assert.Equal("startTls", response.Config.GetProperty("tls").GetString());
        Assert.Equal("••••tery", response.SecretHint);
        Assert.Equal(1, response.Secret.KeyId);
        Assert.Equal("correct-horse-battery", vault.Open(WorkspaceId, RefId, IntegrationType.Smtp, response.Secret).Value.Password);
    }

    [Fact]
    public void Resealing_without_a_secrets_patch_keeps_the_secret_and_reports_it_unchanged()
    {
        var vault = Vault();
        var created = vault.Seal(Request("ntfy", null, ToJson(new { topic = "alerts", accessToken = "tk_abcdefghijkl" }))).Value;

        var resealed = vault.Seal(Request("ntfy", ToJson(new { serverUrl = "https://ntfy.example.test" }), null, created.Secret)).Value;

        Assert.False(resealed.SecretChanged);
        Assert.Equal("https://ntfy.example.test", resealed.Config.GetProperty("serverUrl").GetString());
        Assert.NotEqual(created.Secret.Ciphertext, resealed.Secret.Ciphertext);
        Assert.Equal(new IntegrationSecrets { Topic = "alerts", AccessToken = "tk_abcdefghijkl" }, vault.Open(WorkspaceId, RefId, IntegrationType.Ntfy, resealed.Secret).Value);
    }

    [Fact]
    public void A_patch_that_sets_the_same_value_does_not_count_as_a_change()
    {
        var vault = Vault();
        var created = vault.Seal(Request("ntfy", null, ToJson(new { topic = "alerts" }))).Value;

        var resealed = vault.Seal(Request("ntfy", null, ToJson(new { topic = "alerts" }), created.Secret)).Value;

        Assert.False(resealed.SecretChanged);
    }

    [Fact]
    public void A_patch_replaces_one_field_and_keeps_the_rest()
    {
        var vault = Vault();
        var created = vault.Seal(Request("ntfy", null, ToJson(new { topic = "alerts", accessToken = "tk_old_000000" }))).Value;

        var resealed = vault.Seal(Request("ntfy", null, ToJson(new { accessToken = "tk_new_111111" }), created.Secret)).Value;

        Assert.True(resealed.SecretChanged);
        Assert.Equal("••••", resealed.SecretHint);
        Assert.Equal(new IntegrationSecrets { Topic = "alerts", AccessToken = "tk_new_111111" }, vault.Open(WorkspaceId, RefId, IntegrationType.Ntfy, resealed.Secret).Value);
    }

    [Fact]
    public void An_empty_string_clears_an_optional_field()
    {
        var vault = Vault();
        var created = vault.Seal(Request("ntfy", null, ToJson(new { topic = "alerts", accessToken = "tk_old_000000" }))).Value;

        var resealed = vault.Seal(Request("ntfy", null, ToJson(new { accessToken = "" }), created.Secret)).Value;

        Assert.True(resealed.SecretChanged);
        Assert.Null(vault.Open(WorkspaceId, RefId, IntegrationType.Ntfy, resealed.Secret).Value.AccessToken);
    }

    [Fact]
    public void Clearing_a_required_field_is_refused()
    {
        var vault = Vault();
        var created = vault.Seal(Request("telegram", null, ToJson(new { botToken = BotToken }))).Value;

        var result = vault.Seal(Request("telegram", null, ToJson(new { botToken = "" }), created.Secret));

        Assert.Equal(IntegrationShape.SecretInvalid, result.Error.Code);
    }

    [Fact]
    public void Switching_http_auth_mode_drops_the_old_credential()
    {
        var vault = Vault();
        var created = vault.Seal(Request("httpAuth", ToJson(new { baseUrl = "https://api.example.test", mode = "basic", userName = "svc" }), ToJson(new { password = "pw-0000000000" }))).Value;

        var resealed = vault.Seal(Request("httpAuth", ToJson(new { baseUrl = "https://api.example.test", mode = "bearer" }), ToJson(new { token = "tok-111111111" }), created.Secret)).Value;

        Assert.Equal(new IntegrationSecrets { Token = "tok-111111111" }, vault.Open(WorkspaceId, RefId, IntegrationType.HttpAuth, resealed.Secret).Value);
    }

    [Fact]
    public void A_secret_sealed_for_another_integration_is_unreadable()
    {
        var vault = Vault();
        var other = vault.Seal(Request("telegram", null, ToJson(new { botToken = BotToken })) with { RefId = Guid.NewGuid() }).Value;

        var result = vault.Seal(Request("telegram", null, null, other.Secret));

        Assert.Equal("INTEGRATION_SECRET_UNREADABLE", result.Error.Code);
    }

    [Fact]
    public void A_secret_under_a_key_this_engine_lacks_reads_as_not_configured()
    {
        var sealedUnderKey2 = Vault(Options(2, false, ("2", Key(2)))).Seal(Request("telegram", null, ToJson(new { botToken = BotToken }))).Value;

        var result = Vault().Seal(Request("telegram", null, null, sealedUnderKey2.Secret));

        Assert.Equal("INTEGRATIONS_NOT_CONFIGURED", result.Error.Code);
    }

    [Fact]
    public void Nothing_is_sealed_without_a_master_key()
    {
        var result = Vault(Options(1, false, ("1", ""))).Seal(Request("telegram", null, ToJson(new { botToken = BotToken })));

        Assert.Same(IntegrationVault.NotConfigured, result.Error);
    }

    [Theory]
    [InlineData("Telegram", null, "INTEGRATION_TYPE_INVALID")]
    [InlineData("webhook", null, "INTEGRATION_TYPE_INVALID")]
    [InlineData("httpAuth", "HomeAssistant", "INTEGRATION_PRESET_INVALID")]
    [InlineData("telegram", "homeAssistant", "INTEGRATION_PRESET_INVALID")]
    public void Unknown_types_and_mismatched_presets_are_refused(string type, string? preset, string code)
    {
        var result = Vault().Seal(Request(type, ToJson(new { baseUrl = "https://ha.example.test" }), ToJson(new { token = "t" })) with { Preset = preset });

        Assert.Equal(code, result.Error.Code);
    }

    [Fact]
    public void A_secret_field_of_another_type_is_refused_before_anything_is_opened()
    {
        var result = Vault().Seal(Request("discord", null, ToJson(new { botToken = BotToken })));

        Assert.Equal(IntegrationShape.SecretInvalid, result.Error.Code);
    }

    [Fact]
    public void Home_assistant_is_sealed_as_bearer()
    {
        var response = Vault().Seal(Request("httpAuth", ToJson(new { baseUrl = "https://ha.example.test:8123" }), ToJson(new { token = "llat-abcdefghijk" })) with { Preset = "homeAssistant" }).Value;

        Assert.Equal("bearer", response.Config.GetProperty("mode").GetString());
    }

    // ----------------------------------------------------------------- test

    [Fact]
    public async Task A_draft_can_be_tested_without_a_master_key()
    {
        var channel = new RecordingChannel(IntegrationType.Telegram, ChannelResult.Success(200, ToJson(new { botUsername = "wbskt_bot" })));
        var vault = Vault(Options(1, false, ("1", "")), channel);

        var response = (await vault.TestAsync(Probe("telegram", null, ToJson(new { botToken = BotToken })), CancellationToken.None)).Value;

        Assert.True(response.Ok);
        Assert.Equal("wbskt_bot", response.Metadata!.Value.GetProperty("botUsername").GetString());
        Assert.Equal(BotToken, channel.Tested!.Secrets.BotToken);
    }

    [Fact]
    public async Task A_saved_integration_cannot_be_tested_without_a_master_key()
    {
        var sealedSecret = Vault().Seal(Request("telegram", null, ToJson(new { botToken = BotToken }))).Value.Secret;
        var channel = new RecordingChannel(IntegrationType.Telegram, ChannelResult.Success());

        var result = await Vault(Options(1, false, ("1", "")), channel).TestAsync(Probe("telegram", null, null, sealedSecret), CancellationToken.None);

        Assert.Equal("INTEGRATIONS_NOT_CONFIGURED", result.Error.Code);
        Assert.Null(channel.Tested);
    }

    [Fact]
    public async Task A_test_applies_the_patch_to_what_is_saved()
    {
        var vault = Vault();
        var sealedSecret = vault.Seal(Request("ntfy", null, ToJson(new { topic = "alerts", accessToken = "tk_old_000000" }))).Value.Secret;
        var channel = new RecordingChannel(IntegrationType.Ntfy, ChannelResult.Success());

        await Vault(Options(), channel).TestAsync(Probe("ntfy", null, ToJson(new { accessToken = "tk_new_111111" }), sealedSecret), CancellationToken.None);

        Assert.Equal(new IntegrationSecrets { Topic = "alerts", AccessToken = "tk_new_111111" }, channel.Tested!.Secrets);
        Assert.Equal("https://ntfy.sh", ((NtfySettings)channel.Tested.Settings).ServerUrl);
    }

    [Fact]
    public async Task A_failed_test_is_an_answer_with_the_secret_scrubbed_out()
    {
        var channel = new RecordingChannel(IntegrationType.Telegram, ChannelResult.Fail("INTEGRATION_HTTP_ERROR", $"failed at /bot{BotToken}/getMe", false, 400));

        var response = (await Vault(Options(), channel).TestAsync(Probe("telegram", null, ToJson(new { botToken = BotToken })), CancellationToken.None)).Value;

        Assert.False(response.Ok);
        Assert.Equal("INTEGRATION_HTTP_ERROR", response.Code);
        Assert.Equal("failed at /bot••••/getMe", response.Message);
    }

    [Fact]
    public async Task A_test_of_an_invalid_draft_never_reaches_the_service()
    {
        var channel = new RecordingChannel(IntegrationType.Telegram, ChannelResult.Success());

        var result = await Vault(Options(), channel).TestAsync(Probe("telegram", null, ToJson(new { botToken = "nope" })), CancellationToken.None);

        Assert.Equal(IntegrationShape.SecretInvalid, result.Error.Code);
        Assert.Null(channel.Tested);
    }

    [Theory]
    [InlineData("https://ha.example.test", "https://attacker.example.test")]
    [InlineData("https://ha.example.test", "http://ha.example.test")]
    [InlineData("https://ha.example.test", "https://ha.example.test:8443")]
    public void Moving_an_http_auth_integration_to_another_origin_needs_its_secret_again(string savedUrl, string newUrl)
    {
        var vault = Vault(Options(insecure: true));
        var saved = ToJson(new { baseUrl = savedUrl, mode = "bearer" });
        var created = vault.Seal(Request("httpAuth", saved, ToJson(new { token = "saved-token-123456" }))).Value;

        var moved = vault.Seal(Moved("httpAuth", ToJson(new { baseUrl = newUrl, mode = "bearer" }), null, created.Secret, saved));

        Assert.Equal(IntegrationShape.SecretInvalid, moved.Error.Code);
        Assert.Contains("not carried over", moved.Error.Message);
        Assert.DoesNotContain("saved-token-123456", moved.Error.Message);
    }

    [Fact]
    public void Moving_with_the_secret_given_again_seals_only_the_new_one()
    {
        var vault = Vault();
        var saved = ToJson(new { serverUrl = "https://ntfy.sh" });
        var created = vault.Seal(Request("ntfy", saved, ToJson(new { topic = "alerts", accessToken = "tk_saved_000000" }))).Value;

        var moved = vault.Seal(Moved("ntfy", ToJson(new { serverUrl = "https://ntfy.example.test" }), ToJson(new { topic = "alerts" }), created.Secret, saved)).Value;

        Assert.True(moved.SecretChanged);
        Assert.Equal(new IntegrationSecrets { Topic = "alerts" }, vault.Open(WorkspaceId, RefId, IntegrationType.Ntfy, moved.Secret).Value);
    }

    [Fact]
    public void A_new_path_on_the_same_origin_keeps_the_secret()
    {
        var vault = Vault();
        var saved = ToJson(new { baseUrl = "https://api.example.test/v1", mode = "bearer" });
        var created = vault.Seal(Request("httpAuth", saved, ToJson(new { token = "saved-token-123456" }))).Value;

        var resealed = vault.Seal(Moved("httpAuth", ToJson(new { baseUrl = "https://API.example.test:443/v2", mode = "bearer" }), null, created.Secret, saved)).Value;

        Assert.False(resealed.SecretChanged);
        Assert.Equal("saved-token-123456", vault.Open(WorkspaceId, RefId, IntegrationType.HttpAuth, resealed.Secret).Value.Token);
    }

    [Fact]
    public void Moving_a_mail_relay_to_another_host_or_port_needs_its_password_again()
    {
        var vault = Vault();
        var saved = ToJson(new { host = "smtp.example.test", userName = "svc", fromAddress = "a@example.test" });
        var created = vault.Seal(Request("smtp", saved, ToJson(new { password = "relay-password-123" }))).Value;

        var otherHost = vault.Seal(Moved("smtp", ToJson(new { host = "evil.example.test", userName = "svc", fromAddress = "a@example.test" }), null, created.Secret, saved));
        var otherPort = vault.Seal(Moved("smtp", ToJson(new { host = "smtp.example.test", port = 465, userName = "svc", fromAddress = "a@example.test" }), null, created.Secret, saved));
        var sameRelay = vault.Seal(Moved("smtp", ToJson(new { host = "SMTP.example.test", userName = "svc", fromAddress = "b@example.test" }), null, created.Secret, saved));

        Assert.Equal(IntegrationShape.SecretInvalid, otherHost.Error.Code);
        Assert.Equal(IntegrationShape.SecretInvalid, otherPort.Error.Code);
        Assert.True(sameRelay.IsSuccess);
    }

    [Fact]
    public async Task A_test_against_another_server_never_sends_the_saved_secret()
    {
        var vault = Vault();
        var saved = ToJson(new { baseUrl = "https://ha.example.test", mode = "bearer" });
        var sealedSecret = vault.Seal(Request("httpAuth", saved, ToJson(new { token = "saved-token-123456" }))).Value.Secret;
        var channel = new RecordingChannel(IntegrationType.HttpAuth, ChannelResult.Success());
        var probe = new IntegrationProbeRequest(WorkspaceId, RefId, "httpAuth", null, ToJson(new { baseUrl = "https://attacker.example.test", mode = "bearer" }), null, sealedSecret, null, saved);

        var result = await Vault(Options(), channel).TestAsync(probe, CancellationToken.None);

        Assert.Equal(IntegrationShape.SecretInvalid, result.Error.Code);
        Assert.Null(channel.Tested);
    }

    private static IntegrationSealRequest Moved(string type, JsonElement? config, JsonElement? secrets, SealedSecret existing, JsonElement existingConfig) =>
        new(WorkspaceId, RefId, type, null, config, secrets, existing, existingConfig);

    // ----------------------------------------------------------------- glue

    private static IntegrationVault Vault(IntegrationOptions? options = null, params IIntegrationChannel[] channels)
    {
        options ??= Options();
        return new IntegrationVault(Cipher(options), Microsoft.Extensions.Options.Options.Create(options), channels);
    }

    private static IntegrationSealRequest Request(string type, JsonElement? config, JsonElement? secrets, SealedSecret? existing = null) =>
        new(WorkspaceId, RefId, type, null, config, secrets, existing);

    private static IntegrationProbeRequest Probe(string type, JsonElement? config, JsonElement? secrets, SealedSecret? existing = null) =>
        new(WorkspaceId, RefId, type, null, config, secrets, existing, null);

    private sealed class RecordingChannel(IntegrationType type, ChannelResult result) : IIntegrationChannel
    {
        public ResolvedIntegration? Tested { get; private set; }

        public IntegrationType Type => type;

        public Task<ChannelResult> TestAsync(ResolvedIntegration integration, IntegrationTestOptions? options, CancellationToken ct)
        {
            Tested = integration;
            return Task.FromResult(result);
        }
    }
}
