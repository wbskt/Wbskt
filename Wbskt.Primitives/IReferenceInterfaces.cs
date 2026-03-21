namespace Wbskt.Primitives;

public interface IReferenceProvider
{
    Task<int> FindIdByRefIdAsync(Guid referenceId, CancellationToken cancellationToken = default);
}

public interface IReferenceMapper
{
    Task<int> FindIdByRefIdAsync(Guid referenceId, CancellationToken cancellationToken = default);
}
