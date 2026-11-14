namespace Wbskt.Models.Admin;

/// <summary>
/// Credits staff give a tenant: recorded in the main database as a grant with this
/// <see cref="Source"/> and, as its reference, the idempotency key of the staff change that made it.
/// </summary>
public static class StaffCreditGrants
{
    public const string Source = "staff";

    public const decimal MaxCredits = 100_000m;
}
