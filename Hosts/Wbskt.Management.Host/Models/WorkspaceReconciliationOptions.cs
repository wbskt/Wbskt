namespace Wbskt.Management.Host.Models;

/// <summary>The deleted workspace sweep (<c>DeletedWorkspaceSweeper</c>).</summary>
public sealed class WorkspaceReconciliationOptions
{
    public const string SectionName = "WorkspaceReconciliation";

    /// <summary>How often the sweep runs. It also runs once as the host starts.</summary>
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromDays(1);

    /// <summary>
    /// How far back each sweep asks the auth host for deletes. No watermark is kept: the window is
    /// several intervals long, so a sweep or two that fail (auth down, the host stopped) are covered
    /// by the next, and a workspace already retired is simply passed over.
    /// </summary>
    public int LookbackDays { get; set; } = 7;

    /// <summary>
    /// A delete younger than this is left to its <c>WorkspaceDeletedEvent</c>, which is normally
    /// handled within seconds, so the sweep does not race the handler and count its work as a repair.
    /// </summary>
    public TimeSpan GracePeriod { get; set; } = TimeSpan.FromHours(1);
}
