using Wbskt.Models.Admin;

namespace Wbskt.Infrastructure.StaffActions;

/// <summary>A staff audit row to write.</summary>
public sealed record StaffAuditEntry(StaffChange Change, StaffAction Action, StaffOutcome Outcome, string? Detail);

/// <summary>A staff audit row already written, as an idempotency key finds it.</summary>
public sealed record StaffAuditRow(long Id, string Action, Guid? TargetRefId, StaffOutcome Outcome, string? Detail);
