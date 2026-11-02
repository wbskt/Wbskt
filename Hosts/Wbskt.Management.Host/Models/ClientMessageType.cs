namespace Wbskt.Management.Host.Models;

/// <summary>One row of dbo.ClientMessageTypes.</summary>
public sealed record ClientMessageType(string Type, long Count, DateTime FirstSeenAt, DateTime LastSeenAt, string? SamplePayload);

/// <summary>Messages of one type from one client in a batch, summed, to add to dbo.ClientMessageTypes.</summary>
public sealed record ClientMessageTypeTally(int ClientId, string Type, int Count, DateTime LastSeenAt, string? SamplePayload);
