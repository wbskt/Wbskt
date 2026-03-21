using System.IO;
using System.Security.Cryptography;
using System.Text;
using Wbskt.Client.Sdk;

namespace Wbskt.Client.Windows.Service.Engine;

public class SecureClientStorage : IClientStorage
{
    private static readonly string CredsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
        "Wbskt", 
        "security"
    );
    private static readonly string IdPath = Path.Combine(CredsDir, "client.id");
    private static readonly string SecretPath = Path.Combine(CredsDir, "client.secret");

    public Task SaveCredentialsAsync(Guid refId, string secret)
    {
        if (!Directory.Exists(CredsDir))
        {
            Directory.CreateDirectory(CredsDir);
        }

        File.WriteAllText(IdPath, refId.ToString());
        
        var secretBytes = Encoding.UTF8.GetBytes(secret);
        var encryptedSecret = ProtectedData.Protect(secretBytes, null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(SecretPath, encryptedSecret);

        return Task.CompletedTask;
    }

    public Task<(Guid? RefId, string? Secret)> LoadCredentialsAsync()
    {
        if (!File.Exists(IdPath) || !File.Exists(SecretPath))
        {
            return Task.FromResult<(Guid?, string?)>((null, null));
        }

        try
        {
            var idStr = File.ReadAllText(IdPath);
            var encryptedSecret = File.ReadAllBytes(SecretPath);
            var secretBytes = ProtectedData.Unprotect(encryptedSecret, null, DataProtectionScope.CurrentUser);
            
            return Task.FromResult<(Guid?, string?)>((Guid.Parse(idStr), Encoding.UTF8.GetString(secretBytes)));
        }
        catch
        {
            return Task.FromResult<(Guid?, string?)>((null, null));
        }
    }

    public Task ClearCredentialsAsync()
    {
        if (File.Exists(IdPath))
        {
            File.Delete(IdPath);
        }

        if (File.Exists(SecretPath))
        {
            File.Delete(SecretPath);
        }

        return Task.CompletedTask;
    }
}
