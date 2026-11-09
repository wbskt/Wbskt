using Wbskt.Management.Host.Models;

namespace Wbskt.Management.Host.Providers;

/// <summary>The client messages each account carried this month, and which workspaces were told they passed a share of it.</summary>
public interface IMessageAllowanceProvider
{
    Task<IReadOnlyCollection<WorkspaceMessageAccount>> GetAccountsAsync(DateOnly monthStart, CancellationToken ct);

    /// <summary>Records each workspace as past <paramref name="percent"/> of its allowance this month; returns those not recorded before.</summary>
    Task<IReadOnlyCollection<int>> RecordNoticeAsync(IReadOnlyCollection<int> workspaceIds, DateOnly monthStart, int percent, CancellationToken ct);
}
