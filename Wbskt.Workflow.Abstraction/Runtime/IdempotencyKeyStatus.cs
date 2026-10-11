namespace Wbskt.Workflow.Abstraction.Runtime;

/// <summary>
/// The statuses of a claimed idempotency key (the <c>dbo.IdempotencyKeys.Status</c> column). A key is
/// claimed as Pending and settled once; a Pending key found later means the claimer crashed mid-way.
/// </summary>
public static class IdempotencyKeyStatus
{
    public const string Pending = "Pending";
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";
}
