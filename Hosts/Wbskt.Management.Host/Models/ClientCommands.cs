namespace Wbskt.Management.Host.Models;

// ExpiresAt is optional: past it the command is refused instead of delivered, by the socket host
// and by the SDK. At most 24 hours ahead.
public record ClientCommandRequest(string Type, string Payload, DateTimeOffset? ExpiresAt = null);

public record ClientCommandResponse(Guid CommandId);
