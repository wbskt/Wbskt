using System.Globalization;
using Wbskt.Workflow.Abstraction.Enums;

namespace Wbskt.Workflow.Abstraction.Models.Triggers;

/// <summary>
/// Trigger keys for presence triggers: <c>presence:{clientRef}:{state}:{forSeconds}:{workflowRef}:{nodeId}</c>.
///
/// The key names exactly one trigger node, unlike client-message keys which many workflows share.
/// A presence change does not dispatch straight away: it is parked per trigger until that trigger's
/// own grace period has passed, so each pending check has to say which trigger it is for. The
/// grace period is in the key so the parking side can read it without loading the definition.
/// </summary>
public static class ClientPresenceTriggerKey
{
    public const string TriggerKind = "presence";

    public static string Build(Guid clientRef, ClientPresenceState state, int forSeconds, Guid workflowRefId, Guid triggerNodeId) =>
        string.Create(CultureInfo.InvariantCulture, $"{Prefix(clientRef, state)}{forSeconds}:{workflowRefId}:{triggerNodeId}");

    /// <summary>Every key for this client and state starts with this, whatever its grace period or workflow.</summary>
    public static string Prefix(Guid clientRef, ClientPresenceState state) =>
        $"{TriggerKind}:{clientRef}:{StateName(state)}:";

    public static bool TryGetForSeconds(string triggerKey, out int forSeconds)
    {
        forSeconds = 0;
        string[] parts = triggerKey.Split(':');
        return parts.Length == 6
            && parts[0] == TriggerKind
            && int.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out forSeconds);
    }

    public static string StateName(ClientPresenceState state) => state switch
    {
        ClientPresenceState.Offline => "offline",
        ClientPresenceState.Online => "online",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null)
    };
}
