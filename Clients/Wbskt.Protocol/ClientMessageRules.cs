using System.Diagnostics.CodeAnalysis;

namespace Wbskt.Protocol;

/// <summary>
/// What a message sent to a client may look like, whoever sends it: the console, a message
/// template, or a workflow step. Kept in one place so a workflow cannot send what the console refuses.
/// </summary>
public static class ClientMessageRules
{
    public const int MaxTypeLength = MessageTypes.MaxTypeLength;

    /// <summary>
    /// Half the 64 KB message cap, leaving room for the envelope. A payload under it can still be refused:
    /// what counts is the message as it is sent, escaped, which <see cref="Check"/> measures as well.
    /// </summary>
    public const int MaxPayloadChars = ClientProtocol.MaxMessageBytes / 2;

    // The longest an envelope's other fields can be: a commandId and an expiresAt are on every command.
    private static readonly string LongestCommandId = Guid.Empty.ToString();
    private static readonly DateTimeOffset LongestExpiresAt = DateTimeOffset.MaxValue;

    public static bool IsReserved(string messageType) => MessageTypes.IsReserved(messageType);

    public static bool IsValidType([NotNullWhen(true)] string? messageType) =>
        !string.IsNullOrWhiteSpace(messageType) && messageType.Length <= MaxTypeLength;

    /// <summary>Why a message of this type and payload may not be sent, or null when it may.</summary>
    public static string? FindViolation(string? messageType, string? payload) => Check(messageType, payload)?.Message;

    /// <summary>The rule a message of this type and payload breaks, or null when it may be sent.</summary>
    public static ClientMessageViolation? Check(string? messageType, string? payload)
    {
        if (!IsValidType(messageType))
        {
            return new ClientMessageViolation(ClientMessageRule.TypeLength, $"Message type must be 1-{MaxTypeLength} characters.");
        }

        if (IsReserved(messageType))
        {
            return new ClientMessageViolation(ClientMessageRule.TypeReserved, $"Message type '{messageType}' is reserved for the platform protocol.");
        }

        return CheckPayload(messageType, payload);
    }

    /// <summary>
    /// The rule a platform command (<see cref="MessageTypes.IsPlatformCommand"/>) breaks, or null when
    /// it may be sent. Only the platform's own senders call this; whatever a user or a workflow sends
    /// goes through <see cref="Check"/>, which refuses every reserved type.
    /// </summary>
    public static ClientMessageViolation? CheckPlatformCommand(string? messageType, string? payload)
    {
        if (messageType is null || !MessageTypes.IsPlatformCommand(messageType))
        {
            return new ClientMessageViolation(ClientMessageRule.TypeReserved, $"Message type '{messageType}' is not a platform command.");
        }

        return CheckPayload(messageType, payload);
    }

    private static ClientMessageViolation? CheckPayload(string messageType, string? payload)
    {
        if (payload is { Length: > MaxPayloadChars })
        {
            return new ClientMessageViolation(ClientMessageRule.PayloadSize, $"Message payload is limited to {MaxPayloadChars} characters.");
        }

        // Measured as the payload escaped into a JSON string, the longest it can go out: every quote,
        // backslash and control character in it is escaped, and 32,768 quotes are 64 KB before the
        // envelope. A payload that is JSON goes out as that value, which is never longer.
        if (payload is not null && WireBytes(messageType, payload) > ClientProtocol.MaxMessageBytes)
        {
            return new ClientMessageViolation(ClientMessageRule.PayloadSize, $"Message payload is too large: escaped as it is sent, the message would be over {ClientProtocol.MaxMessageBytes} bytes.");
        }

        return null;
    }

    private static int WireBytes(string messageType, string payload) =>
        ProtocolJson.ByteCount(new ProtocolMessage(messageType, payload, LongestCommandId) { ExpiresAt = LongestExpiresAt });
}

public enum ClientMessageRule
{
    TypeLength,
    TypeReserved,
    PayloadSize
}

public sealed record ClientMessageViolation(ClientMessageRule Rule, string Message);
