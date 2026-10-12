using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Wbskt.Workflow.Abstraction.Integrations;
using Wbskt.Workflow.Engine.Contracts;

namespace Wbskt.Workflow.Integrations;

/// <summary>
/// Seals and opens integration secrets with AES-256-GCM under the configured master keys. The
/// associated data binds a sealed secret to the workspace, the integration's RefId and its type, so
/// a ciphertext copied onto another row, or into another workspace, does not open there.
/// The sealed form is nonce (12) | tag (16) | ciphertext.
/// </summary>
internal sealed class IntegrationCipher
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly IReadOnlyDictionary<byte, byte[]> _keys;
    private readonly byte _activeKeyId;

    public IntegrationCipher(IOptions<IntegrationOptions> options)
    {
        var keys = new Dictionary<byte, byte[]>();
        foreach (var (id, value) in options.Value.Keys)
        {
            if (!byte.TryParse(id, out var keyId) || keyId == 0)
            {
                throw new InvalidOperationException($"{IntegrationOptions.SectionName}:Keys has a key named '{id}'; key ids are 1 to 255.");
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                // An unset environment variable binds as empty: that key is simply not configured.
                continue;
            }

            byte[] key;
            try
            {
                key = Convert.FromBase64String(value);
            }
            catch (FormatException)
            {
                throw new InvalidOperationException($"{IntegrationOptions.SectionName}:Keys:{id} is not base64.");
            }

            if (key.Length != 32)
            {
                throw new InvalidOperationException($"{IntegrationOptions.SectionName}:Keys:{id} must be 32 bytes (AES-256); it is {key.Length}.");
            }

            keys[keyId] = key;
        }

        _keys = keys;
        _activeKeyId = options.Value.ActiveKeyId;
    }

    /// <summary>Whether a secret can be sealed at all: the active key is configured.</summary>
    public bool IsConfigured => _keys.ContainsKey(_activeKeyId);

    public SealedSecret Seal(int workspaceId, Guid refId, IntegrationType type, byte[] plaintext)
    {
        if (!_keys.TryGetValue(_activeKeyId, out var key))
        {
            throw new IntegrationKeyUnavailableException(_activeKeyId);
        }

        var sealedBytes = new byte[NonceSize + TagSize + plaintext.Length];
        var nonce = sealedBytes.AsSpan(0, NonceSize);
        var tag = sealedBytes.AsSpan(NonceSize, TagSize);
        var ciphertext = sealedBytes.AsSpan(NonceSize + TagSize);
        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag, AssociatedData(workspaceId, refId, type));
        return new SealedSecret(sealedBytes, _activeKeyId);
    }

    /// <summary>
    /// Opens a sealed secret. Throws <see cref="IntegrationKeyUnavailableException"/> when its key is
    /// not configured, and <see cref="CryptographicException"/> when it was sealed for another row or
    /// has been tampered with.
    /// </summary>
    public byte[] Open(int workspaceId, Guid refId, IntegrationType type, SealedSecret secret)
    {
        if (!_keys.TryGetValue(secret.KeyId, out var key))
        {
            throw new IntegrationKeyUnavailableException(secret.KeyId);
        }

        var sealedBytes = secret.Ciphertext;
        if (sealedBytes.Length < NonceSize + TagSize)
        {
            throw new CryptographicException("The sealed secret is too short.");
        }

        var plaintext = new byte[sealedBytes.Length - NonceSize - TagSize];
        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(
            sealedBytes.AsSpan(0, NonceSize),
            sealedBytes.AsSpan(NonceSize + TagSize),
            sealedBytes.AsSpan(NonceSize, TagSize),
            plaintext,
            AssociatedData(workspaceId, refId, type));
        return plaintext;
    }

    private static byte[] AssociatedData(int workspaceId, Guid refId, IntegrationType type) =>
        Encoding.UTF8.GetBytes($"wbskt-integration:{workspaceId}:{refId:N}:{(byte)type}");
}

/// <summary>The master key a secret needs is not configured on this engine.</summary>
internal sealed class IntegrationKeyUnavailableException(byte keyId)
    : Exception($"Integration master key {keyId} is not configured ({IntegrationOptions.SectionName}:Keys:{keyId}).")
{
    public byte KeyId { get; } = keyId;
}
