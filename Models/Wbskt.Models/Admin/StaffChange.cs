namespace Wbskt.Models.Admin;

/// <summary>
/// Who on the platform staff asked for a change through the admin host (wbskt/Wbskt.Admin), and why.
/// Every admin action's request carries one. <paramref name="IdempotencyKey"/> is fresh for each form the
/// staff member sends, so a retry of the same call does nothing the second time.
/// </summary>
public sealed record StaffChange(Guid StaffRefId, string StaffName, string Reason, Guid IdempotencyKey);
