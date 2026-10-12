using System.Text.Json.Serialization;

namespace Wbskt.Workflow.Integrations;

/// <summary>
/// An integration's non-secret settings, one record per type, as stored in <c>ConfigJson</c> and
/// shown by the API. Unknown fields are refused rather than dropped, so a misspelt field is an
/// error and not a setting that silently never applies.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal abstract record IntegrationSettings;

/// <summary>A Telegram bot has no settings beyond its token.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record TelegramSettings : IntegrationSettings;

/// <summary>A Discord webhook is all secret: its URL is the credential.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record DiscordSettings : IntegrationSettings;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record SmtpSettings : IntegrationSettings
{
    public const string StartTls = "startTls";
    public const string Implicit = "implicit";

    [JsonPropertyName("host")]
    public string? Host { get; init; }

    [JsonPropertyName("port")]
    public int? Port { get; init; }

    /// <summary><see cref="StartTls"/> (upgrade after connecting, usually 587) or <see cref="Implicit"/> (TLS from the start, usually 465).</summary>
    [JsonPropertyName("tls")]
    public string? Tls { get; init; }

    /// <summary>Optional: a relay that accepts mail without signing in needs none.</summary>
    [JsonPropertyName("userName")]
    public string? UserName { get; init; }

    [JsonPropertyName("fromAddress")]
    public string? FromAddress { get; init; }

    [JsonPropertyName("fromName")]
    public string? FromName { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record NtfySettings : IntegrationSettings
{
    public const string DefaultServerUrl = "https://ntfy.sh";

    [JsonPropertyName("serverUrl")]
    public string? ServerUrl { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record HttpAuthSettings : IntegrationSettings
{
    public const string Bearer = "bearer";
    public const string Basic = "basic";
    public const string Header = "header";

    /// <summary>Where the credential may be sent: a step's URL is relative to it, or shares its origin.</summary>
    [JsonPropertyName("baseUrl")]
    public string? BaseUrl { get; init; }

    /// <summary><see cref="Bearer"/>, <see cref="Basic"/> or <see cref="Header"/>.</summary>
    [JsonPropertyName("mode")]
    public string? Mode { get; init; }

    /// <summary>The header the value goes in, for <see cref="Header"/> mode (e.g. X-Api-Key).</summary>
    [JsonPropertyName("headerName")]
    public string? HeaderName { get; init; }

    /// <summary>The user name, for <see cref="Basic"/> mode.</summary>
    [JsonPropertyName("userName")]
    public string? UserName { get; init; }

    /// <summary>The header the credential travels in.</summary>
    [JsonIgnore]
    public string AuthHeaderName => Mode == Header ? HeaderName! : "Authorization";
}

/// <summary>
/// Every secret field any type has. Each type uses its own few (see <see cref="IntegrationShape"/>);
/// sealed as this record's JSON, with absent fields left out.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record IntegrationSecrets
{
    [JsonPropertyName("botToken")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BotToken { get; init; }

    [JsonPropertyName("webhookUrl")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WebhookUrl { get; init; }

    [JsonPropertyName("password")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Password { get; init; }

    [JsonPropertyName("topic")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Topic { get; init; }

    [JsonPropertyName("accessToken")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AccessToken { get; init; }

    [JsonPropertyName("token")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Token { get; init; }

    [JsonPropertyName("headerValue")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? HeaderValue { get; init; }

    public static readonly IntegrationSecrets Empty = new();

    /// <summary>Each field by its API name, set or not.</summary>
    public IEnumerable<(string Name, string? Value)> Fields()
    {
        yield return ("botToken", BotToken);
        yield return ("webhookUrl", WebhookUrl);
        yield return ("password", Password);
        yield return ("topic", Topic);
        yield return ("accessToken", AccessToken);
        yield return ("token", Token);
        yield return ("headerValue", HeaderValue);
    }

    /// <summary>The set values, for scrubbing them out of anything shown to a person.</summary>
    public IEnumerable<string> Values() => Fields().Select(f => f.Value).Where(v => !string.IsNullOrEmpty(v))!;
}

/// <summary>An integration ready to use: its settings parsed and its secret open.</summary>
internal sealed record ResolvedIntegration(
    int? Id,
    Guid RefId,
    int WorkspaceId,
    Wbskt.Workflow.Abstraction.Integrations.IntegrationType Type,
    Wbskt.Workflow.Abstraction.Integrations.IntegrationPreset? Preset,
    IntegrationSettings Settings,
    IntegrationSecrets Secrets);
