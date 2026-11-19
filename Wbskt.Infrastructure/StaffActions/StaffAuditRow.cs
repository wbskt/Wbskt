using Wbskt.Models.Admin;

namespace Wbskt.Infrastructure.StaffActions;

/// <summary>A staff audit row to write. <paramref name="BodyHash"/> is <see cref="StaffRequestHash"/> of the request.</summary>
public sealed record StaffAuditEntry(StaffChange Change, StaffAction Action, StaffOutcome Outcome, string? Detail, byte[] BodyHash);

/// <summary>
/// A staff audit row already written, as an idempotency key finds it. <paramref name="BodyHash"/> is null
/// for a row written before the hash was kept.
/// </summary>
public sealed record StaffAuditRow(long Id, string Action, Guid? TargetRefId, Guid? SubjectRefId, StaffOutcome Outcome, string? Detail, byte[]? BodyHash);
