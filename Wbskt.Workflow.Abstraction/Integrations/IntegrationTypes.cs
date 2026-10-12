using Wbskt.Workflow.Abstraction.Models.Nodes;

namespace Wbskt.Workflow.Abstraction.Integrations;

/// <summary>
/// The integration types and presets as the API and step configs name them, and which step kind
/// sends through which type. One table both hosts read, so they cannot disagree.
/// </summary>
public static class IntegrationTypes
{
    private static readonly Dictionary<IntegrationType, string> Names = new()
    {
        [IntegrationType.Telegram] = "telegram",
        [IntegrationType.Discord] = "discord",
        [IntegrationType.Smtp] = "smtp",
        [IntegrationType.Ntfy] = "ntfy",
        [IntegrationType.HttpAuth] = "httpAuth"
    };

    private static readonly Dictionary<IntegrationPreset, string> PresetNames = new()
    {
        [IntegrationPreset.HomeAssistant] = "homeAssistant"
    };

    /// <summary>The step kinds that send through an integration, and the type each one takes.</summary>
    private static readonly Dictionary<string, IntegrationType> StepTypes = new(StringComparer.Ordinal)
    {
        [NodeKind.ActionTelegram] = IntegrationType.Telegram,
        [NodeKind.ActionDiscord] = IntegrationType.Discord,
        [NodeKind.ActionEmail] = IntegrationType.Smtp,
        [NodeKind.ActionNtfy] = IntegrationType.Ntfy,
        [NodeKind.ActionWebhook] = IntegrationType.HttpAuth
    };

    /// <summary>Every name the API accepts for a type, in declaration order.</summary>
    public static IReadOnlyCollection<string> AllNames => Names.Values;

    public static string NameOf(IntegrationType type) => Names[type];

    public static string NameOf(IntegrationPreset preset) => PresetNames[preset];

    /// <summary>Case-sensitive, like every other enum-valued field of the API.</summary>
    public static bool TryParse(string? name, out IntegrationType type)
    {
        foreach (var (candidate, candidateName) in Names)
        {
            if (string.Equals(candidateName, name, StringComparison.Ordinal))
            {
                type = candidate;
                return true;
            }
        }

        type = default;
        return false;
    }

    public static bool TryParsePreset(string? name, out IntegrationPreset preset)
    {
        foreach (var (candidate, candidateName) in PresetNames)
        {
            if (string.Equals(candidateName, name, StringComparison.Ordinal))
            {
                preset = candidate;
                return true;
            }
        }

        preset = default;
        return false;
    }

    /// <summary>The preset's type: a preset is always a shape of exactly one type.</summary>
    public static IntegrationType TypeOf(IntegrationPreset preset) => preset switch
    {
        IntegrationPreset.HomeAssistant => IntegrationType.HttpAuth,
        _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, null)
    };

    public static IntegrationCategory CategoryOf(IntegrationType type) => type switch
    {
        IntegrationType.Telegram or IntegrationType.Discord => IntegrationCategory.Chat,
        IntegrationType.Smtp => IntegrationCategory.Email,
        IntegrationType.Ntfy => IntegrationCategory.Push,
        IntegrationType.HttpAuth => IntegrationCategory.Http,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };

    public static string CategoryName(IntegrationType type) => CategoryOf(type) switch
    {
        IntegrationCategory.Chat => "chat",
        IntegrationCategory.Email => "email",
        IntegrationCategory.Push => "push",
        _ => "http"
    };

    /// <summary>The type a step of <paramref name="kind"/> sends through, or null when it sends through none.</summary>
    public static IntegrationType? ForStepKind(string kind) =>
        StepTypes.TryGetValue(kind, out var type) ? type : null;

    /// <summary>
    /// Whether a step of <paramref name="kind"/> cannot run without an integration. Call a URL is the
    /// one step that takes one optionally: without it, it calls the URL with no credential.
    /// </summary>
    public static bool IsRequiredFor(string kind) =>
        StepTypes.ContainsKey(kind) && kind != NodeKind.ActionWebhook;
}
