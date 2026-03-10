namespace Wbskt.Common.Models;

public sealed record EventLogEntry(int EventId, string EventData, DateTime CreatedAtUtc, int? WorkspaceId);
