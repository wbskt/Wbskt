namespace Webskt.Common.Abstraction.Interfaces;

public interface IReferenceProvider
{
    Task<int> FindByReferenceIdAsync(Guid referenceId);
}

public interface IReferenceMapper
{
    Task<int> FindByReferenceIdAsync(Guid referenceId);
}
