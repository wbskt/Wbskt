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
    public static string? FindViolation(string? messageType, string? payload) => Check(messageType, payload)?.Message;

    /// <summary>The rule a message of this type and payload breaks, or null when it may be sent.</summary>
    public static DeviceMessageViolation? Check(string? messageType, string? payload)
    {
        if (!IsValidType(messageType))
        {
            return new DeviceMessageViolation(DeviceMessageRule.TypeLength, $"Message type must be 1-{MaxTypeLength} characters.");
        }

        if (IsReserved(messageType))
        {
            return new DeviceMessageViolation(DeviceMessageRule.TypeReserved, $"Message type '{messageType}' is reserved for the platform protocol.");
        }

        if (payload is { Length: > MaxPayloadChars })
        {
            return new DeviceMessageViolation(DeviceMessageRule.PayloadSize, $"Message payload is limited to {MaxPayloadChars} characters.");
        }

        return null;
    }

    /// <summary>
    /// The rule a platform command (<see cref="MessageTypes.IsPlatformCommand"/>) breaks, or null when
    /// it may be sent. Only the platform's own senders call this; whatever a user or a workflow sends
    /// goes through <see cref="Check"/>, which refuses every reserved type.
    /// </summary>
    public static DeviceMessageViolation? CheckPlatformCommand(string? messageType, string? payload)
    {
        if (messageType is null || !MessageTypes.IsPlatformCommand(messageType))
        {
            return new DeviceMessageViolation(DeviceMessageRule.TypeReserved, $"Message type '{messageType}' is not a platform command.");
        }

        if (payload is { Length: > MaxPayloadChars })
        {
            return new DeviceMessageViolation(DeviceMessageRule.PayloadSize, $"Message payload is limited to {MaxPayloadChars} characters.");
        }

        return null;
    }
}

public enum DeviceMessageRule
{
    TypeLength,
    TypeReserved,
    PayloadSize
}

public sealed record DeviceMessageViolation(DeviceMessageRule Rule, string Message);
