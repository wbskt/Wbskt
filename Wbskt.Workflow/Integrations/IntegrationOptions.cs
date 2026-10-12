namespace Wbskt.Workflow.Integrations;

/// <summary>
/// The engine's integration settings, bound from <c>WorkflowEngine:Integrations</c>.
/// </summary>
public sealed class IntegrationOptions
{
    public const string SectionName = "WorkflowEngine:Integrations";

    private static readonly string[] DefaultDiscordHosts = ["discord.com", "discordapp.com"];
    private static readonly int[] DefaultSmtpPorts = [25, 465, 587, 2525];

    /// <summary>
    /// The master keys, by key id (1-255): each a base64 32-byte AES-256 key. Only the engine has
    /// them. In production they come from the environment
    /// (<c>WorkflowEngine__Integrations__Keys__1</c>), never from a file in the image.
    /// </summary>
    public Dictionary<string, string> Keys { get; init; } = new();

    /// <summary>The key new and resealed secrets are sealed with. Older ones open with the id stored beside them.</summary>
    public byte ActiveKeyId { get; init; } = 1;

    /// <summary>Overridable for testing and for a self-hosted Bot API server. The bot token only ever goes here.</summary>
    public string TelegramApiBaseUrl { get; init; } = "https://api.telegram.org";

    /// <summary>The hosts a Discord webhook URL may point at. Empty means the default (discord.com, discordapp.com).</summary>
    public string[]? DiscordHosts { get; init; }

    /// <summary>The SMTP ports a relay may be on. Empty means the default (25, 465, 587, 2525).</summary>
    public int[]? SmtpPorts { get; init; }

    /// <summary>
    /// Development and E2E only: allows plain http for Discord, ntfy and HTTP auth targets, any SMTP
    /// port, and SMTP without TLS, so local fakes can stand in for the real services. Production
    /// leaves it unset.
    /// </summary>
    public bool AllowInsecureTransport { get; init; }

    public IReadOnlyCollection<string> EffectiveDiscordHosts => DiscordHosts is { Length: > 0 } ? DiscordHosts : DefaultDiscordHosts;

    public IReadOnlyCollection<int> EffectiveSmtpPorts => SmtpPorts is { Length: > 0 } ? SmtpPorts : DefaultSmtpPorts;
}
