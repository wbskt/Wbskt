namespace Wbskt.Management.Host.Models;

/// <summary>An account's client messages this month beside its plan's allowance and the point it is throttled at.</summary>
/// <param name="ThrottleAt">The messages a month at which the account is throttled; null for a plan that never is.</param>
public sealed record MessageAllowanceUsage(WorkspaceMessageAccount Account, long Allowance, decimal? ThrottleAt)
{
    public long Used => Account.AccountMessages;

    public bool IsThrottled => ThrottleAt is { } at && Used >= at;
}
