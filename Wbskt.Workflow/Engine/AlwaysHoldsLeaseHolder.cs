using Wbskt.Workflow.Abstraction.Engine;

namespace Wbskt.Workflow.Engine;

public sealed class AlwaysHoldsLeaseHolder : ILeaseHolder
{
    public Task<bool> TryAcquireAsync(string leaseName, CancellationToken ct)
    {
        return Task.FromResult(true);
    }

    public Task ReleaseAsync(string leaseName, CancellationToken ct)
    {
        return Task.CompletedTask;
    }

    public Task<bool> IsHeldAsync(string leaseName, CancellationToken ct)
    {
        return Task.FromResult(true);
    }
}
