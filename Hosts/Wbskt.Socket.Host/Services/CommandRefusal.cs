using System.Text.Json;

namespace Wbskt.Socket.Host.Services;

/// <summary>
/// Reads the refusal an SDK puts on a <c>sys.ack</c> (<c>{ commandId, type, refused }</c>) when it
/// will not act on a command. Both strings come from the device, so they are length-capped before
/// they reach the event log.
/// </summary>
internal static class CommandRefusal
{
    private const int MaxTypeLength = 100;
    private const int MaxReasonLength = 100;

    public static bool TryRead(object payload, out string type, out string reason)
    {
        type = string.Empty;
        reason = string.Empty;

        if (payload is not JsonElement { ValueKind: JsonValueKind.Object } element
            || !element.TryGetProperty("refused", out var refused)
            || refused.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(refused.GetString()))
        {
            return false;
        }

        reason = $"Refused by the device: {Cap(refused.GetString()!, MaxReasonLength)}";
        if (element.TryGetProperty("type", out var typeElement) && typeElement.ValueKind == JsonValueKind.String)
        {
            type = Cap(typeElement.GetString() ?? string.Empty, MaxTypeLength);
        }

        return true;
    }

    private static string Cap(string value, int max) => value.Length <= max ? value : value[..max];
}
