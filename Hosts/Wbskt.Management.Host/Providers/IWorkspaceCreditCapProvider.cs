namespace Wbskt.Management.Host.Providers;

/// <summary>A workspace's monthly credit cap (<c>dbo.WorkspaceCreditCaps</c>).</summary>
public interface IWorkspaceCreditCapProvider
{
    /// <summary>
    /// Sets the cap, or removes it when <paramref name="monthlyCreditCap"/> is null, and returns what the
    /// workspace has spent since <paramref name="monthStart"/>.
    /// </summary>
    Task<decimal> SetAsync(int workspaceId, decimal? monthlyCreditCap, DateOnly monthStart, CancellationToken ct);
}
