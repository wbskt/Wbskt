using Wbskt.Common.Records;

namespace Wbskt.Common.Writers;

public interface ICredentialsWriter
{
    Task UpsertAsync(CredentialRecord credential, CancellationToken cancellationToken);
    Task DeleteAsync(int id, int userId, CancellationToken cancellationToken);
}
