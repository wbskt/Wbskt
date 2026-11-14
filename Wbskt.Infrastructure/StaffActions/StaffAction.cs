namespace Wbskt.Infrastructure.StaffActions;

/// <summary>
/// The change a staff member asked for, as the staff audit records it: <paramref name="Name"/> as
/// <c>SetTenantLimits</c>, and what it is about (<paramref name="TargetKind"/> as <c>Account</c>,
/// <c>Person</c> or <c>Workspace</c>, and its RefId).
/// </summary>
public sealed record StaffAction(string Name, string TargetKind, Guid? TargetRefId);
