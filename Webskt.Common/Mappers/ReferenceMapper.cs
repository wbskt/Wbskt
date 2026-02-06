using Webskt.Common.Abstraction.Interfaces;

namespace Webskt.Common.Mappers;

public sealed class ReferenceMapper<T> : IReferenceMapper where T : class, IReferenceProvider
{
    private readonly T _referenceProvider;

    public ReferenceMapper(T target)
    {
        _referenceProvider = target ?? throw new ArgumentNullException(nameof(target));
    }

    public Task<int> FindByReferenceIdAsync(Guid referenceId)
    {
        return _referenceProvider.FindByReferenceIdAsync(referenceId);
    }
}
