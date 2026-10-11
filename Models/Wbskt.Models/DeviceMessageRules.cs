using System.Diagnostics.CodeAnalysis;

namespace Wbskt.Models;

/// <summary>
/// What a message sent to a device may look like, whoever sends it: the console, a message
/// template, or a workflow step. Kept in one place so a workflow cannot send what the console refuses.
/// </summary>
public static class DeviceMessageRules
{
    // Message types owned by the platform protocol. Devices and users must not send them as
    // ordinary payloads: sys.* frames never reach the generic pipeline, and "capabilities" /
    // "state.report" are destructured by ClientMetadataIngestionHandler.
    public const string Capabilities = "capabilities";
    public const string StateReport = "state.report";
    public const string SystemPrefix = "sys.";

    public const int MaxTypeLength = 100;

    /// <summary>Half the 64 KB socket message cap, leaving room for the envelope.</summary>
    public const int MaxPayloadChars = 32 * 1024;

    public static bool IsReserved(string messageType)
    {
        return messageType.StartsWith(SystemPrefix, StringComparison.OrdinalIgnoreCase)
               || messageType.Equals(Capabilities, StringComparison.OrdinalIgnoreCase)
               || messageType.Equals(StateReport, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsValidType([NotNullWhen(true)] string? messageType) =>
        !string.IsNullOrWhiteSpace(messageType) && messageType.Length <= MaxTypeLength;

    /// <summary>Why a message of this type and payload may not be sent, or null when it may.</summary>
    public static string? FindViolation(string? messageType, string? payload)
    {
        if (!IsValidType(messageType))
        {
            return $"Message type must be 1-{MaxTypeLength} characters.";
        }

        if (IsReserved(messageType))
        {
            return $"Message type '{messageType}' is reserved for the platform protocol.";
        }

        if (payload is { Length: > MaxPayloadChars })
        {
            return $"Message payload is limited to {MaxPayloadChars} characters.";
        }

        return null;
    }
}
