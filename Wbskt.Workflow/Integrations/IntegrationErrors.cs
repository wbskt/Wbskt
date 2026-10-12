namespace Wbskt.Workflow.Integrations;

/// <summary>
/// Makes a message safe to show and keep: every secret value the integration holds is blanked out,
/// and the length is capped to what dbo.Integrations.LastTestError and run history should carry.
/// A service's error text is never trusted not to quote the request, and a Telegram request's URL
/// carries the bot token.
/// </summary>
internal static class IntegrationErrors
{
    public const int MaxLength = 400;

    public static string Scrub(string? message, IntegrationSecrets secrets)
    {
        string text = message ?? string.Empty;
        foreach (var secret in secrets.Values().OrderByDescending(v => v.Length))
        {
            if (secret.Length >= 4)
            {
                text = text.Replace(secret, "••••", StringComparison.Ordinal);
            }
        }

        return text.Length <= MaxLength ? text : text[..(MaxLength - 1)] + "…";
    }

    /// <summary><paramref name="result"/> with its message scrubbed of <paramref name="integration"/>'s secrets.</summary>
    public static ChannelResult Scrubbed(ChannelResult result, ResolvedIntegration integration) =>
        result with { Message = Scrub(result.Message, integration.Secrets) };
}

/// <summary>
/// What a send or a test came to. A failure carries a stable code, a message already scrubbed of
/// secrets, and whether trying again could help.
/// </summary>
internal sealed record ChannelResult(bool Ok, string? Code, string? Message, bool Retryable, int? Status = null, System.Text.Json.JsonElement? Metadata = null)
{
    public static ChannelResult Success(int? status = null, System.Text.Json.JsonElement? metadata = null) => new(true, null, null, false, status, metadata);

    public static ChannelResult Fail(string code, string message, bool retryable, int? status = null) => new(false, code, message, retryable, status);
}
