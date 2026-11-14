namespace Wbskt.Infrastructure.StaffActions;

/// <summary>
/// The change a staff member asked for, as the staff audit records it: <paramref name="Name"/> as
/// <c>SetTenantLimits</c>, and what it is about (<paramref name="TargetKind"/> as <c>Account</c>,
/// <c>Person</c> or <c>Workspace</c>, and its RefId). <paramref name="Detail"/> is what else a successful
/// change's row records, such as the member an account change was about; a refused row records the refusal.
/// </summary>
public sealed record StaffAction(string Name, string TargetKind, Guid? TargetRefId, string? Detail = null);
