using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Tokens;
using Wbskt.Infrastructure.Security;

namespace Wbskt.Workflow.Engine.Host.Tests.Infrastructure;

public sealed class JwtTrustTests
{
    private static readonly Claim[] UserClaims = [new(ClaimTypes.NameIdentifier, "42"), new("type", "user")];

    [Fact]
    public async Task A_token_signed_by_the_issuer_for_the_audience_is_accepted()
    {
        using var keys = NewKeys();
        var token = new JwtService(keys, JwtIssuers.Auth).GenerateToken(UserClaims, JwtAudiences.Api, TimeSpan.FromMinutes(15));

        var principal = await JwtTrust.Local(JwtIssuers.Auth, JwtAudiences.Api, keys.PublishedKeys).ValidateAsync(token);

        Assert.Equal("42", principal.FindFirst(ClaimTypes.NameIdentifier)?.Value);
        var jwt = new JsonWebToken(token);
        Assert.Equal("ES256", jwt.Alg);
        Assert.Equal(keys.KeyId, jwt.Kid);
        Assert.Equal(JwtIssuers.Auth, jwt.Issuer);
        Assert.Equal([JwtAudiences.Api], jwt.Audiences);
        Assert.False(string.IsNullOrEmpty(jwt.Id));
    }

    [Fact]
    public async Task A_token_for_another_audience_is_rejected()
    {
        // The socket host's view of a user token: right signature, wrong surface.
        using var keys = NewKeys();
        var token = new JwtService(keys, JwtIssuers.Auth).GenerateToken(UserClaims, JwtAudiences.Api, TimeSpan.FromMinutes(15));

        await Assert.ThrowsAsync<SecurityTokenInvalidAudienceException>(
            () => JwtTrust.Local(JwtIssuers.Auth, JwtAudiences.Socket, keys.PublishedKeys).ValidateAsync(token));
    }

    [Fact]
    public async Task A_token_from_another_issuer_is_rejected_even_when_the_key_is_trusted()
    {
        using var keys = NewKeys();
        var token = new JwtService(keys, JwtIssuers.Management).GenerateToken(UserClaims, JwtAudiences.Api, TimeSpan.FromMinutes(15));

        await Assert.ThrowsAsync<SecurityTokenInvalidIssuerException>(
            () => JwtTrust.Local(JwtIssuers.Auth, JwtAudiences.Api, keys.PublishedKeys).ValidateAsync(token));
    }

    [Fact]
    public async Task A_token_signed_by_a_key_the_issuer_does_not_publish_is_rejected()
    {
        using var issuer = NewKeys();
        using var forger = NewKeys();
        var token = new JwtService(forger, JwtIssuers.Auth).GenerateToken(UserClaims, JwtAudiences.Api, TimeSpan.FromMinutes(15));

        await Assert.ThrowsAnyAsync<SecurityTokenException>(
            () => JwtTrust.Local(JwtIssuers.Auth, JwtAudiences.Api, issuer.PublishedKeys).ValidateAsync(token));
    }

    [Fact]
    public async Task A_token_signed_with_the_old_shared_secret_is_rejected()
    {
        using var keys = NewKeys();
        var hmac = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(new string('k', 64)));
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(UserClaims),
            Issuer = JwtIssuers.Auth,
            Audience = JwtAudiences.Api,
            Expires = DateTime.UtcNow.AddMinutes(15),
            SigningCredentials = new SigningCredentials(hmac, SecurityAlgorithms.HmacSha256)
        });

        await Assert.ThrowsAnyAsync<SecurityTokenException>(
            () => JwtTrust.Local(JwtIssuers.Auth, JwtAudiences.Api, keys.PublishedKeys).ValidateAsync(token));
    }

    [Fact]
    public async Task An_expired_token_is_rejected()
    {
        using var keys = NewKeys();
        var handler = new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false };
        var token = handler.CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(UserClaims),
            Issuer = JwtIssuers.Auth,
            Audience = JwtAudiences.Api,
            IssuedAt = DateTime.UtcNow.AddMinutes(-20),
            Expires = DateTime.UtcNow.AddMinutes(-5),
            SigningCredentials = keys.SigningCredentials
        });

        await Assert.ThrowsAsync<SecurityTokenExpiredException>(
            () => JwtTrust.Local(JwtIssuers.Auth, JwtAudiences.Api, keys.PublishedKeys).ValidateAsync(token));
    }

    [Fact]
    public async Task A_remote_trust_validates_against_the_published_jwks()
    {
        using var keys = NewKeys();
        var documents = new JwksDocuments { Json = JsonSerializer.Serialize(keys.ToJwks()) };
        var trust = JwtTrust.Remote(JwtIssuers.Management, JwtAudiences.Socket, "http://management/.well-known/jwks.json", documents);
        var token = new JwtService(keys, JwtIssuers.Management).GenerateToken([new Claim("type", "client")], JwtAudiences.Socket, TimeSpan.FromHours(1));

        var principal = await trust.ValidateAsync(token);

        Assert.Equal("client", principal.FindFirst("type")?.Value);
    }

    [Fact]
    public async Task A_rotated_key_is_picked_up_without_restarting_the_validator()
    {
        using var oldKeys = NewKeys();
        var documents = new JwksDocuments { Json = JsonSerializer.Serialize(oldKeys.ToJwks()) };
        var trust = JwtTrust.Remote(JwtIssuers.Management, JwtAudiences.Socket, "http://management/.well-known/jwks.json", documents);
        await trust.ValidateAsync(new JwtService(oldKeys, JwtIssuers.Management).GenerateToken([], JwtAudiences.Socket, TimeSpan.FromHours(1)));

        // The issuer restarts on a new key, still publishing the old one.
        using var newKeys = new JwtSigningKeys(Pem(ECDsa.Create(ECCurve.NamedCurves.nistP256)), [PublicPem(oldKeys)], false, NullLogger.Instance);
        documents.Json = JsonSerializer.Serialize(newKeys.ToJwks());
        var token = new JwtService(newKeys, JwtIssuers.Management).GenerateToken([], JwtAudiences.Socket, TimeSpan.FromHours(1));

        // The unknown kid requests a refresh; depending on the IdentityModel version the refetch can
        // complete in the background, so the first attempt after a rotation may still miss.
        ClaimsPrincipal? principal = null;
        for (var attempt = 0; attempt < 20 && principal is null; attempt++)
        {
            try
            {
                principal = await trust.ValidateAsync(token);
            }
            catch (SecurityTokenException)
            {
                await Task.Delay(50);
            }
        }

        Assert.NotNull(principal);
        Assert.True(documents.Fetches >= 2);
    }

    [Fact]
    public void A_host_that_does_not_issue_the_tokens_it_trusts_needs_a_jwks_url()
    {
        // The management host signs as wbskt-management but accepts user tokens from wbskt-auth.
        using var keys = NewKeys();
        var configuration = new ConfigurationBuilder().Build();

        var ex = Assert.Throws<InvalidOperationException>(
            () => JwtTrust.Create(configuration, JwtIssuers.Auth, JwtAudiences.Api, new LocalJwtIssuer(JwtIssuers.Management, keys)));
        Assert.Contains(JwtTrust.TrustedJwksUrlConfig, ex.Message);
    }

    [Fact]
    public void The_issuing_host_trusts_its_own_keys_without_a_url()
    {
        using var keys = NewKeys();

        var trust = JwtTrust.Create(new ConfigurationBuilder().Build(), JwtIssuers.Auth, JwtAudiences.Api, new LocalJwtIssuer(JwtIssuers.Auth, keys));

        Assert.Null(trust.ConfigurationManager);
    }

    [Fact]
    public void Outside_development_a_missing_signing_key_fails_startup()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => new JwtSigningKeys(null, [], allowEphemeral: false, NullLogger.Instance));
        Assert.Contains(JwtSigningKeys.SigningKeyConfig, ex.Message);
    }

    [Fact]
    public void Only_P256_keys_are_accepted()
    {
        using var p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);

        Assert.Throws<InvalidOperationException>(() => new JwtSigningKeys(Pem(p384), [], false, NullLogger.Instance));
    }

    [Fact]
    public void A_base64_encoded_pem_loads_and_the_key_id_is_stable()
    {
        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pem = Pem(ec);
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(pem));

        using var fromPem = new JwtSigningKeys(pem, [], false, NullLogger.Instance);
        using var fromBase64 = new JwtSigningKeys(base64, [], false, NullLogger.Instance);

        Assert.Equal(fromPem.KeyId, fromBase64.KeyId);
    }

    [Fact]
    public void The_jwks_publishes_public_parameters_only()
    {
        using var keys = NewKeys();

        var json = JsonSerializer.Serialize(keys.ToJwks());

        using var doc = JsonDocument.Parse(json);
        var key = Assert.Single(doc.RootElement.GetProperty("keys").EnumerateArray());
        Assert.False(key.TryGetProperty("d", out _));
        Assert.Equal(keys.KeyId, key.GetProperty("kid").GetString());
    }

    private static JwtSigningKeys NewKeys() => new(Pem(ECDsa.Create(ECCurve.NamedCurves.nistP256)), [], false, NullLogger.Instance);

    private static string Pem(ECDsa key) => key.ExportPkcs8PrivateKeyPem();

    private static string PublicPem(JwtSigningKeys keys) =>
        ((ECDsaSecurityKey)keys.PublishedKeys[0]).ECDsa.ExportSubjectPublicKeyInfoPem();

    private sealed class JwksDocuments : IDocumentRetriever
    {
        public required string Json { get; set; }

        public int Fetches { get; private set; }

        public Task<string> GetDocumentAsync(string address, CancellationToken cancel)
        {
            Fetches++;
            return Task.FromResult(Json);
        }
    }
}
