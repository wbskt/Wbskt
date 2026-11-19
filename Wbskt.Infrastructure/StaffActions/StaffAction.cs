namespace Wbskt.Infrastructure.StaffActions;

/// <summary>
/// The change a staff member asked for, as the staff audit records it: <paramref name="Name"/> as
/// <c>SetTenantLimits</c>, and what it is about (<paramref name="TargetKind"/> as <c>Account</c>,
/// <c>Person</c> or <c>Workspace</c>, and its RefId). <paramref name="SubjectRefId"/> is what within the
/// target it is about, such as the member of an account or one of its credit grants, so the person's own
/// page finds a change filed under their account. <paramref name="Detail"/> is what else a successful
/// change's row records; it holds references, not names or addresses, since the audit outlives the people
/// in it. A refused row records the refusal's code before it.
/// </summary>
public sealed record StaffAction(string Name, string TargetKind, Guid? TargetRefId, string? Detail = null, Guid? SubjectRefId = null);
