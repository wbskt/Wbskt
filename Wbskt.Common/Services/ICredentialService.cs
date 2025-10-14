namespace Wbskt.Common.Services;

public interface ICredentialService
{
    Task<string> GetCredentialsAsync(int userId, string integrationName, CancellationToken cancellationToken);
    Task SaveCredentialsAsync(int userId, string integrationType, string name, string credentials, CancellationToken cancellationToken);
}
