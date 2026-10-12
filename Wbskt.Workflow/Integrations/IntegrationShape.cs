using System.Text.Json;
using System.Text.RegularExpressions;
using MimeKit;
using Wbskt.Infrastructure;
using Wbskt.Workflow.Abstraction.Integrations;

namespace Wbskt.Workflow.Integrations;

/// <summary>
/// What a valid integration of each type looks like: its settings parsed with their defaults, its
/// secret fields, and the transport rules (TLS, allowed hosts and ports) this engine enforces. The
/// engine is the one place this is decided, since it is where the secret is sealed and used.
/// </summary>
internal static partial class IntegrationShape
{
    public const string ConfigInvalid = "INTEGRATION_CONFIG_INVALID";
    public const string SecretInvalid = "INTEGRATION_SECRET_INVALID";
    public const string PresetInvalid = "INTEGRATION_PRESET_INVALID";

    private const int MaxSecretLength = 4096;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Request headers an integration may not put its credential in: the HTTP stack owns them.</summary>
    private static readonly HashSet<string> ReservedHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Host", "Content-Length", "Content-Type", "Transfer-Encoding", "Connection", "Keep-Alive", "Upgrade",
        "TE", "Trailer", "Proxy-Connection", "Expect", "Cookie"
    };

    public static Result<IntegrationSettings> ParseSettings(IntegrationType type, IntegrationPreset? preset, JsonElement? config, IntegrationOptions options)
    {
        if (preset is { } p && IntegrationTypes.TypeOf(p) != type)
        {
            return Fail<IntegrationSettings>(PresetInvalid, $"The {IntegrationTypes.NameOf(p)} preset is not a {IntegrationTypes.NameOf(type)} integration.");
        }

        JsonElement element = config is { ValueKind: not (JsonValueKind.Undefined or JsonValueKind.Null) } c
            ? c
            : JsonDocument.Parse("{}").RootElement;
        if (element.ValueKind != JsonValueKind.Object)
        {
            return Fail<IntegrationSettings>(ConfigInvalid, "config must be an object.");
        }

        IntegrationSettings? settings;
        try
        {
            settings = type switch
            {
                IntegrationType.Telegram => element.Deserialize<TelegramSettings>(Json),
                IntegrationType.Discord => element.Deserialize<DiscordSettings>(Json),
                IntegrationType.Smtp => element.Deserialize<SmtpSettings>(Json),
                IntegrationType.Ntfy => element.Deserialize<NtfySettings>(Json),
                IntegrationType.HttpAuth => element.Deserialize<HttpAuthSettings>(Json),
                _ => null
            };
        }
        catch (JsonException ex)
        {
            return Fail<IntegrationSettings>(ConfigInvalid, $"config is not valid for {IntegrationTypes.NameOf(type)}: {FirstSentence(ex.Message)}");
        }

        return settings switch
        {
            SmtpSettings smtp => Smtp(smtp, options),
            NtfySettings ntfy => Ntfy(ntfy, options),
            HttpAuthSettings http => HttpAuth(http, preset, options),
            null => Fail<IntegrationSettings>(ConfigInvalid, "config is missing."),
            _ => Result<IntegrationSettings>.Success(settings)
        };
    }

    public static Result<IntegrationSecrets> ParseSecrets(JsonElement? secrets)
    {
        if (secrets is not { ValueKind: not (JsonValueKind.Undefined or JsonValueKind.Null) } element)
        {
            return Result<IntegrationSecrets>.Success(IntegrationSecrets.Empty);
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return Fail<IntegrationSecrets>(SecretInvalid, "secrets must be an object.");
        }

        try
        {
            return Result<IntegrationSecrets>.Success(element.Deserialize<IntegrationSecrets>(Json) ?? IntegrationSecrets.Empty);
        }
        catch (JsonException ex)
        {
            // The message names the field, never its value.
            return Fail<IntegrationSecrets>(SecretInvalid, $"secrets are not valid: {FirstSentence(ex.Message)}");
        }
    }

    /// <summary>A patch over stored secrets: a field it sets replaces the stored one, an empty string clears it.</summary>
    public static IntegrationSecrets Merge(IntegrationSecrets existing, IntegrationSecrets patch)
    {
        static string? Pick(string? stored, string? patched) => patched is null ? stored : patched.Length == 0 ? null : patched;

        return new IntegrationSecrets
        {
            BotToken = Pick(existing.BotToken, patch.BotToken),
            WebhookUrl = Pick(existing.WebhookUrl, patch.WebhookUrl),
            Password = Pick(existing.Password, patch.Password),
            Topic = Pick(existing.Topic, patch.Topic),
            AccessToken = Pick(existing.AccessToken, patch.AccessToken),
            Token = Pick(existing.Token, patch.Token),
            HeaderValue = Pick(existing.HeaderValue, patch.HeaderValue)
        };
    }

    /// <summary>The secret fields a type has, by API name.</summary>
    public static IReadOnlySet<string> SecretFieldsOf(IntegrationType type) => type switch
    {
        IntegrationType.Telegram => new HashSet<string> { "botToken" },
        IntegrationType.Discord => new HashSet<string> { "webhookUrl" },
        IntegrationType.Smtp => new HashSet<string> { "password" },
        IntegrationType.Ntfy => new HashSet<string> { "topic", "accessToken" },
        IntegrationType.HttpAuth => new HashSet<string> { "token", "password", "headerValue" },
        _ => new HashSet<string>()
    };

    /// <summary>Refuses a secret field the type does not have, so a misplaced one is not silently sealed.</summary>
    public static Result CheckFields(IntegrationType type, IntegrationSecrets secrets)
    {
        var allowed = SecretFieldsOf(type);
        foreach (var (name, value) in secrets.Fields())
        {
            if (value is not null && !allowed.Contains(name))
            {
                return Result.Failure(Error.Validation(SecretInvalid, $"'{name}' is not a {IntegrationTypes.NameOf(type)} secret. Its secrets are {string.Join(", ", allowed)}."));
            }
        }

        return Result.Success();
    }

    /// <summary>
    /// The complete secret checked against the settings it goes with. HTTP auth keeps only what its
    /// mode uses, so switching mode drops the old credential rather than sealing it unused.
    /// </summary>
    public static Result<IntegrationSecrets> ValidateSecrets(IntegrationType type, IntegrationSettings settings, IntegrationSecrets secrets, IntegrationOptions options)
    {
        foreach (var value in secrets.Values())
        {
            if (value.Length > MaxSecretLength || value.Any(char.IsControl))
            {
                return Fail<IntegrationSecrets>(SecretInvalid, $"A secret must be at most {MaxSecretLength} characters, with no line breaks or control characters.");
            }
        }

        switch (type, settings)
        {
            case (IntegrationType.Telegram, _):
                if (string.IsNullOrEmpty(secrets.BotToken))
                {
                    return Fail<IntegrationSecrets>(SecretInvalid, "botToken is required.");
                }

                if (!TelegramToken().IsMatch(secrets.BotToken))
                {
                    return Fail<IntegrationSecrets>(SecretInvalid, "botToken does not look like a Telegram bot token (digits, a colon, then the key BotFather gave you).");
                }

                return Result<IntegrationSecrets>.Success(secrets);

            case (IntegrationType.Discord, _):
                return string.IsNullOrEmpty(secrets.WebhookUrl)
                    ? Fail<IntegrationSecrets>(SecretInvalid, "webhookUrl is required.")
                    : DiscordWebhook(secrets.WebhookUrl, options) is { } discordProblem
                        ? Fail<IntegrationSecrets>(SecretInvalid, discordProblem)
                        : Result<IntegrationSecrets>.Success(secrets);

            case (IntegrationType.Smtp, SmtpSettings smtp):
                if (smtp.UserName is not null && string.IsNullOrEmpty(secrets.Password))
                {
                    return Fail<IntegrationSecrets>(SecretInvalid, "password is required when userName is set.");
                }

                if (smtp.UserName is null && secrets.Password is not null)
                {
                    return Fail<IntegrationSecrets>(SecretInvalid, "password needs a userName to sign in with.");
                }

                return Result<IntegrationSecrets>.Success(secrets);

            case (IntegrationType.Ntfy, _):
                if (string.IsNullOrEmpty(secrets.Topic) || !NtfyTopic().IsMatch(secrets.Topic))
                {
                    return Fail<IntegrationSecrets>(SecretInvalid, "topic is required: 1-64 letters, digits, '-' or '_'.");
                }

                return Result<IntegrationSecrets>.Success(secrets);

            case (IntegrationType.HttpAuth, HttpAuthSettings http):
                var kept = http.Mode switch
                {
                    HttpAuthSettings.Bearer => new IntegrationSecrets { Token = secrets.Token },
                    HttpAuthSettings.Basic => new IntegrationSecrets { Password = secrets.Password },
                    _ => new IntegrationSecrets { HeaderValue = secrets.HeaderValue }
                };
                string field = http.Mode switch
                {
                    HttpAuthSettings.Bearer => "token",
                    HttpAuthSettings.Basic => "password",
                    _ => "headerValue"
                };
                return kept.Values().Any()
                    ? Result<IntegrationSecrets>.Success(kept)
                    : Fail<IntegrationSecrets>(SecretInvalid, $"{field} is required for {http.Mode} mode.");

            default:
                return Fail<IntegrationSecrets>(ConfigInvalid, "The settings do not match the integration's type.");
        }
    }

    /// <summary>
    /// Enough of the secret to tell which one is saved: its last four characters. Null when the type's
    /// secret is optional and none is set.
    /// </summary>
    public static string? Hint(IntegrationType type, IntegrationSecrets secrets)
    {
        string? primary = type switch
        {
            IntegrationType.Telegram => secrets.BotToken,
            IntegrationType.Discord => secrets.WebhookUrl,
            IntegrationType.Smtp => secrets.Password,
            IntegrationType.Ntfy => secrets.Topic,
            _ => secrets.Token ?? secrets.Password ?? secrets.HeaderValue
        };

        if (string.IsNullOrEmpty(primary))
        {
            return null;
        }

        // A short secret would be mostly given away by four characters.
        return primary.Length >= 12 ? "••••" + primary[^4..] : "••••";
    }

    /// <summary>
    /// Where the settings send the credential: the HTTP origin of an API or ntfy server, or an SMTP
    /// relay's host and port. Null for types whose destination is the secret itself (a Telegram token
    /// only goes to Telegram, a Discord webhook URL is the credential).
    /// </summary>
    public static string? Destination(IntegrationSettings settings) => settings switch
    {
        HttpAuthSettings http => Origin(http.BaseUrl),
        NtfySettings ntfy => Origin(ntfy.ServerUrl),
        SmtpSettings smtp => $"{smtp.Host?.ToLowerInvariant()}:{smtp.Port}",
        _ => null
    };

    private static string? Origin(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.GetLeftPart(UriPartial.Authority).ToLowerInvariant() : url;

    private static Result<IntegrationSettings> Smtp(SmtpSettings smtp, IntegrationOptions options)
    {
        string? host = smtp.Host?.Trim();
        if (string.IsNullOrEmpty(host) || host.Length > 253 || Uri.CheckHostName(host) == UriHostNameType.Unknown)
        {
            return Fail<IntegrationSettings>(ConfigInvalid, "host must be the relay's host name.");
        }

        int port = smtp.Port ?? 587;
        if (port is < 1 or > 65535)
        {
            return Fail<IntegrationSettings>(ConfigInvalid, "port must be 1 to 65535.");
        }

        if (!options.AllowInsecureTransport && !options.EffectiveSmtpPorts.Contains(port))
        {
            return Fail<IntegrationSettings>(ConfigInvalid, $"port must be one of {string.Join(", ", options.EffectiveSmtpPorts)}.");
        }

        string tls = smtp.Tls ?? (port == 465 ? SmtpSettings.Implicit : SmtpSettings.StartTls);
        if (tls is not (SmtpSettings.StartTls or SmtpSettings.Implicit))
        {
            return Fail<IntegrationSettings>(ConfigInvalid, $"tls must be '{SmtpSettings.StartTls}' or '{SmtpSettings.Implicit}'.");
        }

        string? userName = string.IsNullOrWhiteSpace(smtp.UserName) ? null : smtp.UserName.Trim();
        if (userName is { Length: > 256 })
        {
            return Fail<IntegrationSettings>(ConfigInvalid, "userName must be at most 256 characters.");
        }

        if (string.IsNullOrWhiteSpace(smtp.FromAddress) || !MailboxAddress.TryParse(smtp.FromAddress.Trim(), out var from) || from.Address.Contains(' ') || !from.Address.Contains('@'))
        {
            return Fail<IntegrationSettings>(ConfigInvalid, "fromAddress must be an email address the relay will send as.");
        }

        string? fromName = string.IsNullOrWhiteSpace(smtp.FromName) ? null : smtp.FromName.Trim();
        if (fromName is { Length: > 100 } || fromName?.Any(char.IsControl) == true)
        {
            return Fail<IntegrationSettings>(ConfigInvalid, "fromName must be at most 100 characters on one line.");
        }

        return Result<IntegrationSettings>.Success(new SmtpSettings
        {
            Host = host,
            Port = port,
            Tls = tls,
            UserName = userName,
            FromAddress = from.Address,
            FromName = fromName
        });
    }

    private static Result<IntegrationSettings> Ntfy(NtfySettings ntfy, IntegrationOptions options)
    {
        string raw = string.IsNullOrWhiteSpace(ntfy.ServerUrl) ? NtfySettings.DefaultServerUrl : ntfy.ServerUrl.Trim();
        if (BaseUrl(raw, "serverUrl", options) is { } problem)
        {
            return Fail<IntegrationSettings>(ConfigInvalid, problem);
        }

        return Result<IntegrationSettings>.Success(new NtfySettings { ServerUrl = raw.TrimEnd('/') });
    }

    private static Result<IntegrationSettings> HttpAuth(HttpAuthSettings http, IntegrationPreset? preset, IntegrationOptions options)
    {
        string? baseUrl = http.BaseUrl?.Trim();
        if (string.IsNullOrEmpty(baseUrl))
        {
            return Fail<IntegrationSettings>(ConfigInvalid, "baseUrl is required.");
        }

        if (BaseUrl(baseUrl, "baseUrl", options) is { } problem)
        {
            return Fail<IntegrationSettings>(ConfigInvalid, problem);
        }

        // Home Assistant takes a long-lived access token as a bearer token, and nothing else.
        string? mode = http.Mode ?? (preset == IntegrationPreset.HomeAssistant ? HttpAuthSettings.Bearer : null);
        if (preset == IntegrationPreset.HomeAssistant && mode != HttpAuthSettings.Bearer)
        {
            return Fail<IntegrationSettings>(ConfigInvalid, "Home Assistant uses bearer mode (a long-lived access token).");
        }

        switch (mode)
        {
            case HttpAuthSettings.Bearer:
                return Result<IntegrationSettings>.Success(new HttpAuthSettings { BaseUrl = baseUrl.TrimEnd('/'), Mode = mode });

            case HttpAuthSettings.Basic:
                if (string.IsNullOrWhiteSpace(http.UserName) || http.UserName.Contains(':') || http.UserName.Length > 256 || http.UserName.Any(char.IsControl))
                {
                    return Fail<IntegrationSettings>(ConfigInvalid, "userName is required for basic mode, without a ':'.");
                }

                return Result<IntegrationSettings>.Success(new HttpAuthSettings { BaseUrl = baseUrl.TrimEnd('/'), Mode = mode, UserName = http.UserName.Trim() });

            case HttpAuthSettings.Header:
                string? headerName = http.HeaderName?.Trim();
                if (string.IsNullOrEmpty(headerName) || !HeaderToken().IsMatch(headerName))
                {
                    return Fail<IntegrationSettings>(ConfigInvalid, "headerName must be a header name, such as X-Api-Key.");
                }

                if (ReservedHeaders.Contains(headerName))
                {
                    return Fail<IntegrationSettings>(ConfigInvalid, $"'{headerName}' is set by the request itself and cannot carry a credential.");
                }

                return Result<IntegrationSettings>.Success(new HttpAuthSettings { BaseUrl = baseUrl.TrimEnd('/'), Mode = mode, HeaderName = headerName });

            default:
                return Fail<IntegrationSettings>(ConfigInvalid, $"mode must be '{HttpAuthSettings.Bearer}', '{HttpAuthSettings.Basic}' or '{HttpAuthSettings.Header}'.");
        }
    }

    /// <summary>An absolute https URL (http too in development) with no credentials, query or fragment.</summary>
    private static string? BaseUrl(string raw, string field, IntegrationOptions options)
    {
        if (raw.Length > 2000 || !Uri.TryCreate(raw, UriKind.Absolute, out var uri))
        {
            return $"{field} must be an absolute URL.";
        }

        if (uri.Scheme != Uri.UriSchemeHttps && !(options.AllowInsecureTransport && uri.Scheme == Uri.UriSchemeHttp))
        {
            return $"{field} must be an https URL.";
        }

        if (!string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            return $"{field} must not carry a user name, query or fragment.";
        }

        return null;
    }

    private static string? DiscordWebhook(string raw, IntegrationOptions options)
    {
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri))
        {
            return "webhookUrl must be the URL Discord shows for the webhook.";
        }

        if (uri.Scheme != Uri.UriSchemeHttps && !(options.AllowInsecureTransport && uri.Scheme == Uri.UriSchemeHttp))
        {
            return "webhookUrl must be an https URL.";
        }

        bool hostAllowed = options.EffectiveDiscordHosts.Any(h =>
            uri.Host.Equals(h, StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith("." + h, StringComparison.OrdinalIgnoreCase));
        if (!hostAllowed || !uri.AbsolutePath.StartsWith("/api/webhooks/", StringComparison.Ordinal))
        {
            return "webhookUrl must be a Discord webhook URL (https://discord.com/api/webhooks/...).";
        }

        return string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Fragment)
            ? null
            : "webhookUrl must not carry a user name or fragment.";
    }

    private static Result<T> Fail<T>(string code, string message) => Result<T>.Failure(Error.Validation(code, message));

    // System.Text.Json's messages run on with a path and line numbers; the first sentence is the useful part.
    private static string FirstSentence(string message)
    {
        int end = message.IndexOf(". ", StringComparison.Ordinal);
        return end < 0 ? message : message[..(end + 1)];
    }

    [GeneratedRegex(@"^\d{3,20}:[A-Za-z0-9_-]{20,100}$")]
    private static partial Regex TelegramToken();

    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$")]
    private static partial Regex NtfyTopic();

    [GeneratedRegex(@"^[A-Za-z0-9!#$%&'*+\-.^_`|~]{1,100}$")]
    private static partial Regex HeaderToken();
}
