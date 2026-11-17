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

    public static int ByteCount(ProtocolMessage message) => JsonSerializer.SerializeToUtf8Bytes(message, Options).Length;
}
