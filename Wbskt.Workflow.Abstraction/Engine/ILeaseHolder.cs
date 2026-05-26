namespace Wbskt.Workflow.Abstraction.Engine;

public interface ILeaseHolder
{
    Task<bool> TryAcquireAsync(string leaseName, CancellationToken ct);
    Task ReleaseAsync(string leaseName, CancellationToken ct);
    Task<bool> IsHeldAsync(string leaseName, CancellationToken ct);
}
