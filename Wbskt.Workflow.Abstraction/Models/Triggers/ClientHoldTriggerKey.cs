using System.Globalization;

namespace Wbskt.Workflow.Abstraction.Models.Triggers;

/// <summary>
/// Trigger keys for client triggers with a hold time: <c>hold:{clientRef}:{type}:{holdSeconds}:{workflowRef}:{nodeId}</c>.
///
/// Like presence keys, and unlike plain client-message keys, a hold key names exactly one trigger
/// node: each one tracks its own "filter has matched since" state. The hold time sits in the key so
/// the side recording messages can read it without loading the definition. The message type is
/// author-chosen and may itself contain ':', so the fixed fields are read from the end.
/// </summary>
public static class ClientHoldTriggerKey
{
    public const string TriggerKind = "client-hold";

    private const string KeyTag = "hold";

    public static string Build(Guid clientRef, string type, int holdSeconds, Guid workflowRefId, Guid triggerNodeId) =>
        string.Create(CultureInfo.InvariantCulture, $"{Prefix(clientRef, type)}{holdSeconds}:{workflowRefId}:{triggerNodeId}");

    /// <summary>Every key for this client and message type starts with this; <c>*</c> is the any-type trigger.</summary>
    public static string Prefix(Guid clientRef, string type) =>
        $"{KeyTag}:{clientRef}:{type}:";

    public static bool TryGetHoldSeconds(string triggerKey, out int holdSeconds)
    {
        holdSeconds = 0;
        string[] parts = triggerKey.Split(':');
        return parts.Length >= 6
            && parts[0] == KeyTag
            && int.TryParse(parts[^3], NumberStyles.None, CultureInfo.InvariantCulture, out holdSeconds);
    }
}
