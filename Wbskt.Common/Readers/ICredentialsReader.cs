using Wbskt.Common.Records;

namespace Wbskt.Common.Readers;

public interface ICredentialsReader
{
    Task<string?> GetEncryptedCredentialsAsync(int userId, string name, CancellationToken cancellationToken);
    Task<List<CredentialRecord>> GetAllForUserAsync(int userId, CancellationToken cancellationToken);
}
