namespace Wbskt.Management.Models;

/// <summary>
/// Sets a workspace's monthly credit cap, or removes it with <c>MonthlyCreditCap</c> null. A ceiling on
/// what the workspace may spend of its account's shared credits in a calendar month (UTC), not a
/// reservation: the caps of an account's workspaces may add up to more than it has.
/// </summary>
public sealed record WorkspaceCreditCapRequest(decimal? MonthlyCreditCap);

/// <param name="MonthlyCreditCap">The cap now; null for none.</param>
/// <param name="CreditsSpentThisMonth">What the workspace has spent this calendar month (UTC).</param>
/// <param name="CapReached">True when the cap is at or below that, so the workspace's new runs are refused from now on.</param>
public sealed record WorkspaceCreditCapResponse(decimal? MonthlyCreditCap, decimal CreditsSpentThisMonth, bool CapReached);
