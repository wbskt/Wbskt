namespace Wbskt.Primitives;

public enum ReferenceType
{
    Workspace,
    RegistrationPolicy,
    Client,
}

public interface IReferenceMapper
{
    Task<int> FindIdByRefIdAsync(Guid referenceId, CancellationToken cancellationToken = default);
}
