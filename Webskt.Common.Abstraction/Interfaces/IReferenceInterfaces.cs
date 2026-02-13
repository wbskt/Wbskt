namespace Webskt.Common.Abstraction.Interfaces;

public interface IReferenceProvider
{
    Task<int> FindByReferenceIdAsync(Guid referenceId, CancellationToken cancellationToken = default);
}

public interface IReferenceMapper
{
    Task<int> FindByReferenceIdAsync(Guid referenceId, CancellationToken cancellationToken = default);
}
