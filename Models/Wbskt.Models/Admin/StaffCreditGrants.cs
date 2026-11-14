namespace Wbskt.Models.Admin;

/// <summary>
/// Credits staff give a tenant: recorded in the main database as a grant with this
/// <see cref="Source"/> and the reference <see cref="SourceRef"/> makes.
/// </summary>
public static class StaffCreditGrants
{
    public const string Source = "staff";

    public const decimal MaxCredits = 100_000m;

    /// <summary>
    /// A staff grant's reference: the tenant's RefId, then the grant's (the idempotency key of the change
    /// that gave it). A revoke builds it from the tenant in its route, so it can only reach that tenant's grant.
    /// </summary>
    public static string SourceRef(Guid tenantRef, Guid grantRef) => $"{tenantRef:D}/{grantRef:D}";
}
