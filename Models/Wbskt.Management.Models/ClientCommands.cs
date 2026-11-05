namespace Wbskt.Management.Models;

// ExpiresAt is optional: past it the command is refused instead of delivered, by the socket host
// and by the SDK. At most 24 hours ahead. Left out, a command for an offline client waits 24 hours.
public record ClientCommandRequest(string Type, string Payload, DateTimeOffset? ExpiresAt = null);

/// <param name="Status"><see cref="ClientCommandStatus.Sent"/>, or <see cref="ClientCommandStatus.Queued"/> when the client is offline.</param>
/// <param name="ExpiresAt">When a queued command stops waiting for the client; the request's expiry for a sent one.</param>
public record ClientCommandResponse(Guid CommandId, string Status, DateTimeOffset? ExpiresAt);

/// <summary>A firmware update for one client: where the image is, and its digest.</summary>
/// <param name="Url">An <c>https</c> URL the client can download the image from.</param>
/// <param name="Sha256">The image's SHA-256 as 64 hex digits (<c>sha256sum firmware.bin</c>).</param>
/// <param name="Version">The version the new firmware reports. Optional.</param>
/// <param name="Size">The image's size in bytes, so the client can refuse one too big before downloading. Optional.</param>
/// <param name="ExpiresAt">Don't install after this. Defaults to the command queue's expiry.</param>
public record ClientOtaRequest(string Url, string Sha256, string? Version = null, long? Size = null, DateTimeOffset? ExpiresAt = null);

/// <summary>A command waiting for an offline client.</summary>
public record QueuedClientCommandResponse(Guid CommandId, string Type, string Payload, DateTimeOffset QueuedAt, DateTimeOffset ExpiresAt);

public static class ClientCommandStatus
{
    /// <summary>On its way to the connected client.</summary>
    public const string Sent = "sent";

    /// <summary>Waiting for the offline client to connect.</summary>
    public const string Queued = "queued";
}
