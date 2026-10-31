namespace Wbskt.Management.Models;

// ExpiresAt is optional: past it the command is refused instead of delivered, by the socket host
// and by the SDK. At most 24 hours ahead. Left out, a command for an offline device waits 24 hours.
public record ClientCommandRequest(string Type, string Payload, DateTimeOffset? ExpiresAt = null);

/// <param name="Status"><see cref="ClientCommandStatus.Sent"/>, or <see cref="ClientCommandStatus.Queued"/> when the device is offline.</param>
/// <param name="ExpiresAt">When a queued command stops waiting for the device; the request's expiry for a sent one.</param>
public record ClientCommandResponse(Guid CommandId, string Status = ClientCommandStatus.Sent, DateTimeOffset? ExpiresAt = null);

/// <summary>A command waiting for an offline device.</summary>
public record QueuedClientCommandResponse(Guid CommandId, string Type, string Payload, DateTimeOffset QueuedAt, DateTimeOffset ExpiresAt);

public static class ClientCommandStatus
{
    /// <summary>On its way to the connected device.</summary>
    public const string Sent = "sent";

    /// <summary>Waiting for the offline device to connect.</summary>
    public const string Queued = "queued";
}
