using Microsoft.AspNetCore.DataProtection;
using Wbskt.Common.Readers;
using Wbskt.Common.Records;
using Wbskt.Common.Writers;

namespace Wbskt.Common.Services.Implementations;

public class CredentialService : ICredentialService
{
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly ICredentialsReader _credentialsReader;
    private readonly ICredentialsWriter _credentialsWriter;

    public CredentialService(IDataProtectionProvider dataProtectionProvider, ICredentialsReader credentialsReader, ICredentialsWriter credentialsWriter)
    {
        _dataProtectionProvider = dataProtectionProvider;
        _credentialsReader = credentialsReader;
        _credentialsWriter = credentialsWriter;
    }

    public async Task<string> GetCredentialsAsync(int userId, string integrationName, CancellationToken cancellationToken)
    {
        var encryptedValueFromDb = await _credentialsReader.GetEncryptedCredentialsAsync(userId, integrationName, cancellationToken);
        if (string.IsNullOrEmpty(encryptedValueFromDb))
        {
            throw new InvalidOperationException($"Credentials named '{integrationName}' not found for the user.");
        }

        var protector = _dataProtectionProvider.CreateProtector("Wbskt.Credentials");
        var decrypted = protector.Unprotect(encryptedValueFromDb);
        return decrypted;
    }

    public Task SaveCredentialsAsync(int userId, string integrationType, string name, string credentials, CancellationToken cancellationToken)
    {
        var protector = _dataProtectionProvider.CreateProtector("Wbskt.Credentials");
        var encryptedCredentials = protector.Protect(credentials);

        var record = new CredentialRecord
        {
            UserId = userId,
            IntegrationType = integrationType,
            Name = name,
            EncryptedCredentials = encryptedCredentials
        };

        return _credentialsWriter.UpsertAsync(record, cancellationToken);
    }
}
