using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace Wbskt.Infrastructure.Security;

/// <summary>
/// The key pair a token-issuing host signs with, plus every public key it publishes. Singleton:
/// loaded once at startup, so a missing or malformed key fails the host before it serves anything.
/// </summary>
/// <remarks>
/// <para>
/// <c>Jwt:SigningKey</c> is an EC P-256 private key (ES256) in PEM, either as-is or base64-encoded so
/// it fits on one line of an env file. Generate one with
/// <c>openssl ecparam -name prime256v1 -genkey -noout | openssl pkcs8 -topk8 -nocrypt | base64 -w0</c>.
/// </para>
/// <para>
/// Rotation needs no coordinated restart. Move the old key's public half into
/// <c>Jwt:AdditionalPublicKeys</c>, put the new private key in <c>Jwt:SigningKey</c>, and restart the
/// issuing host alone: validators see a <c>kid</c> they do not know, refetch the JWKS, and find both.
/// Remove the old public key once every token it signed has expired.
/// </para>
/// <para>
/// With no key configured a Development host generates a throwaway one, so a fresh checkout runs.
/// Anywhere else that is a startup failure: an ephemeral key would invalidate every session on each
/// restart and differ between replicas, which looks like random sign-outs rather than a config error.
/// </para>
/// </remarks>
public sealed class JwtSigningKeys : IDisposable
{
    public const string SigningKeyConfig = "Jwt:SigningKey";
    public const string AdditionalPublicKeysConfig = "Jwt:AdditionalPublicKeys";

    private readonly ECDsa _privateKey;
    private readonly List<ECDsa> _publicKeys = [];

    public JwtSigningKeys(IConfiguration configuration, IHostEnvironment environment, ILogger<JwtSigningKeys> logger)
        : this(configuration[SigningKeyConfig], configuration.GetSection(AdditionalPublicKeysConfig).Get<string[]>() ?? [], environment.IsDevelopment(), logger)
    {
    }

    internal JwtSigningKeys(string? signingKey, IEnumerable<string> additionalPublicKeys, bool allowEphemeral, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(signingKey))
        {
            if (!allowEphemeral)
            {
                throw new InvalidOperationException(
                    $"{SigningKeyConfig} is not configured. Every token-issuing host needs its own EC P-256 private key; see deploy/README.md.");
            }

            logger.LogWarning("{Config} is not configured; generated an ephemeral signing key. Tokens will not survive a restart.", SigningKeyConfig);
            _privateKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        }
        else
        {
            _privateKey = ImportEcKey(signingKey, SigningKeyConfig);
            if (_privateKey.ExportParameters(includePrivateParameters: false).Curve.Oid.Value != ECCurve.NamedCurves.nistP256.Oid.Value)
            {
                throw new InvalidOperationException($"{SigningKeyConfig} must be a P-256 key; ES256 is the only algorithm accepted.");
            }
        }

        var signingKeyPublic = ToPublicKey(_privateKey);
        KeyId = ComputeKeyId(signingKeyPublic);
        SigningCredentials = new SigningCredentials(new ECDsaSecurityKey(_privateKey) { KeyId = KeyId }, SecurityAlgorithms.EcdsaSha256);

        var published = new List<SecurityKey> { new ECDsaSecurityKey(signingKeyPublic) { KeyId = KeyId } };
        _publicKeys.Add(signingKeyPublic);

        foreach (var pem in additionalPublicKeys.Where(k => !string.IsNullOrWhiteSpace(k)))
        {
            var key = ToPublicKey(ImportEcKey(pem, AdditionalPublicKeysConfig));
            _publicKeys.Add(key);
            published.Add(new ECDsaSecurityKey(key) { KeyId = ComputeKeyId(key) });
        }

        PublishedKeys = published;
    }

    /// <summary>The <c>kid</c> stamped on every token this host signs: the key's RFC 7638 thumbprint.</summary>
    public string KeyId { get; }

    public SigningCredentials SigningCredentials { get; }

    /// <summary>Public keys only - the current one and any still-valid retired ones.</summary>
    public IReadOnlyList<SecurityKey> PublishedKeys { get; }

    /// <summary>The JWKS document validators fetch: public parameters only, never <c>d</c>.</summary>
    public object ToJwks() => new
    {
        keys = PublishedKeys.Cast<ECDsaSecurityKey>().Select(key =>
        {
            var p = key.ECDsa.ExportParameters(includePrivateParameters: false);
            return new
            {
                kty = "EC",
                use = "sig",
                alg = SecurityAlgorithms.EcdsaSha256,
                crv = "P-256",
                kid = key.KeyId,
                x = Base64UrlEncoder.Encode(p.Q.X),
                y = Base64UrlEncoder.Encode(p.Q.Y)
            };
        })
    };

    public void Dispose()
    {
        _privateKey.Dispose();
        foreach (var key in _publicKeys)
        {
            key.Dispose();
        }
    }

    private static ECDsa ImportEcKey(string value, string configKey)
    {
        var pem = value.Trim();

        // One line in an env file: either base64 of the whole PEM, or the PEM with literal \n escapes.
        if (!pem.StartsWith("-----", StringComparison.Ordinal))
        {
            try
            {
                pem = Encoding.UTF8.GetString(Convert.FromBase64String(pem));
            }
            catch (FormatException)
            {
                throw new InvalidOperationException($"{configKey} is neither a PEM key nor base64 of one.");
            }
        }

        pem = pem.Replace("\\n", "\n");

        var key = ECDsa.Create();
        try
        {
            key.ImportFromPem(pem);
        }
        catch (Exception ex) when (ex is ArgumentException or CryptographicException)
        {
            key.Dispose();
            throw new InvalidOperationException($"{configKey} could not be read as an EC key in PEM form.", ex);
        }

        return key;
    }

    private static ECDsa ToPublicKey(ECDsa key) => ECDsa.Create(key.ExportParameters(includePrivateParameters: false));

    private static string ComputeKeyId(ECDsa publicKey)
    {
        var jwk = JsonWebKeyConverter.ConvertFromECDsaSecurityKey(new ECDsaSecurityKey(publicKey));
        return Base64UrlEncoder.Encode(jwk.ComputeJwkThumbprint());
    }
}
