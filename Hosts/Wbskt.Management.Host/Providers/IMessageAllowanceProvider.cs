using Wbskt.Management.Host.Models;

namespace Wbskt.Management.Host.Providers;

/// <summary>The client messages each account carried this month, and which workspaces were told they reached the limit.</summary>
public interface IMessageAllowanceProvider
{
    Task<IReadOnlyCollection<WorkspaceMessageAccount>> GetAccountsAsync(DateOnly monthStart, CancellationToken ct);

    /// <summary>Records each workspace as over its allowance this month; returns those not recorded before.</summary>
    Task<IReadOnlyCollection<int>> RecordLimitReachedAsync(IReadOnlyCollection<int> workspaceIds, DateOnly monthStart, CancellationToken ct);
}
