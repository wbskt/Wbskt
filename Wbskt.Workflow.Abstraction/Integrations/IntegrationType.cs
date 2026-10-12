namespace Wbskt.Workflow.Abstraction.Integrations;

/// <summary>
/// The kind of outside account an integration holds. Stored as a TINYINT (dbo.Integrations.Type),
/// so the values are append-only: never renumber or reuse one. The API speaks the names in
/// <see cref="IntegrationTypes"/>, never the numbers.
/// </summary>
public enum IntegrationType : byte
{
    Telegram = 1,
    Discord = 2,
    Smtp = 3,
    Ntfy = 4,
    HttpAuth = 5
}

/// <summary>
/// A named shape of an integration type, with its own defaults and test. Append-only like
/// <see cref="IntegrationType"/>.
/// </summary>
public enum IntegrationPreset : byte
{
    /// <summary>An <see cref="IntegrationType.HttpAuth"/> bearer token for a Home Assistant instance.</summary>
    HomeAssistant = 1
}

/// <summary>How the console groups integrations.</summary>
public enum IntegrationCategory
{
    Chat,
    Email,
    Push,
    Http
}
