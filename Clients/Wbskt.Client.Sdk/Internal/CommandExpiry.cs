using System.Text.Json;

namespace Wbskt.Client.Sdk.Internal;

/// <summary>
/// Reads a command frame's optional <c>expiresAt</c>. A command past it is refused, not raised: a
/// "start the pump" that arrives after its moment has passed must not be acted on. A missing or
/// unreadable value means the command does not expire, so a bad timestamp never drops a command.
/// </summary>
internal static class CommandExpiry
{
    public static bool IsExpired(JsonElement frame, DateTimeOffset now)
    {
        return frame.ValueKind == JsonValueKind.Object
            && frame.TryGetProperty("expiresAt", out var expiresAt)
            && expiresAt.ValueKind == JsonValueKind.String
            && expiresAt.TryGetDateTimeOffset(out var value)
            && value <= now;
    }
}
