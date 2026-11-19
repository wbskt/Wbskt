namespace Wbskt.Models.Admin;

/// <summary>
/// The body of a change a staff member asks for through the admin host. The staff runner keeps a hash of
/// it, without the idempotency key, so a retry that carries the key of an earlier, different body is
/// refused rather than answered with the earlier change.
/// </summary>
public interface IStaffRequest
{
    StaffChange Staff { get; }
}
