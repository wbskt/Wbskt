namespace Wbskt.Workflow.Abstraction.Providers;

public interface ILeaseProvider
{
    Task<bool> TryAcquireAsync(string leaseName, string holderId, TimeSpan ttl, CancellationToken ct);
    Task ReleaseAsync(string leaseName, string holderId, CancellationToken ct);
}
