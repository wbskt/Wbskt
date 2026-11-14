namespace Wbskt.Models.Admin;

/// <summary>
/// What an admin action did: the staff audit row it wrote, and whether this call was a retry of one
/// already done (<paramref name="Replayed"/>), in which case nothing ran again.
/// </summary>
public sealed record StaffActionResponse(long AuditId, bool Replayed);
