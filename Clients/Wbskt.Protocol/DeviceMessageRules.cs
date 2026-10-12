using System.Diagnostics.CodeAnalysis;

namespace Wbskt.Protocol;

/// <summary>
/// What a message sent to a device may look like, whoever sends it: the console, a message
/// template, or a workflow step. Kept in one place so a workflow cannot send what the console refuses.
/// </summary>
public static class DeviceMessageRules
{
    public const int MaxTypeLength = MessageTypes.MaxTypeLength;

    /// <summary>Half the 64 KB socket message cap, leaving room for the envelope.</summary>
    public const int MaxPayloadChars = SocketProtocol.MaxMessageBytes / 2;

    public static bool IsReserved(string messageType) => MessageTypes.IsReserved(messageType);

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
