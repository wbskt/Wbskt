using System.Text.Encodings.Web;
using System.Text.Json;

namespace Wbskt.Protocol;

/// <summary>
/// How the platform writes a message to a client, so the size checked before sending is the size sent.
/// </summary>
public static class ProtocolJson
{
    // The default encoder writes every non-ASCII character as a six-byte \uXXXX escape, so a payload of
    // accented or CJK text grew to several times its length on the wire. Relaxed escaping writes it as
    // UTF-8 and escapes only what JSON requires, which every SDK's parser reads the same.
    private static readonly JsonSerializerOptions Options = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string Serialize(ProtocolMessage message) => JsonSerializer.Serialize(message, Options);

    /// <summary>
    /// A payload as JSON text. One read from a client is passed through as the client wrote it; anything
    /// else is written with the same relaxed escaping as a message.
    /// </summary>
    public static string SerializePayload(object? payload) =>
        payload is JsonElement element ? element.GetRawText() : JsonSerializer.Serialize(payload, Options);

    public static int ByteCount(ProtocolMessage message) => JsonSerializer.SerializeToUtf8Bytes(message, Options).Length;
}
