namespace Wbskt.Primitives;

public interface IReferenceProvider
{
    /// <summary>The internal id the reference names, or null when it names nothing.</summary>
    Task<int?> FindIdByRefIdAsync(Guid referenceId, CancellationToken cancellationToken = default);
}