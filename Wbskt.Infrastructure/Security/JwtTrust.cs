using System.Security.Claims;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Wbskt.Infrastructure.Security;

/// <summary>
/// The one kind of token a host accepts: signed by <see cref="Issuer"/> for <see cref="Audience"/>,
/// ES256 only. Singleton, shared by the JWT bearer handler and anything that validates by hand.
/// </summary>
/// <remarks>
/// The keys come from one of two places. A host that is itself the issuer validates against its own
/// <see cref="JwtSigningKeys"/>. Any other host fetches the issuer's JWKS from
/// <c>Jwt:TrustedJwksUrl</c> and caches it; a token carrying a <c>kid</c> it has not seen triggers a
/// refetch (at most once per <see cref="RefreshInterval"/>), which is what lets the issuer rotate its
/// key without restarting anyone else.
/// </remarks>
public sealed class JwtTrust
{
    public const string TrustedJwksUrlConfig = "Jwt:TrustedJwksUrl";

    /// <summary>The floor between two key refetches triggered by an unknown <c>kid</c>.</summary>
    internal static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(30);

    /// <summary>Tolerated clock difference between hosts. The default five minutes is a third of an access token's life.</summary>
    internal static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(30);

    private readonly IReadOnlyList<SecurityKey>? _localKeys;
    private readonly JsonWebTokenHandler _handler = new();

    private JwtTrust(string issuer, string audience, IReadOnlyList<SecurityKey>? localKeys, ConfigurationManager<OpenIdConnectConfiguration>? remote)
    {
        Issuer = issuer;
        Audience = audience;
        _localKeys = localKeys;
        ConfigurationManager = remote;
    }

    public string Issuer { get; }

    public string Audience { get; }

    /// <summary>Set when the keys are fetched from another host; null when this host is the issuer.</summary>
    public ConfigurationManager<OpenIdConnectConfiguration>? ConfigurationManager { get; }

    /// <summary>Trusts tokens this host signed itself.</summary>
    public static JwtTrust Local(string issuer, string audience, IReadOnlyList<SecurityKey> keys) => new(issuer, audience, keys, null);

    /// <summary>Trusts tokens signed by the keys published at <paramref name="jwksUrl"/>.</summary>
    public static JwtTrust Remote(string issuer, string audience, string jwksUrl, IDocumentRetriever? documents = null)
    {
        var manager = new ConfigurationManager<OpenIdConnectConfiguration>(
            jwksUrl,
            new JwksRetriever(issuer),
            // Plain HTTP is deliberate: the JWKS is fetched over the compose network, where nothing
            // terminates TLS. The document holds public keys only.
            documents ?? new HttpDocumentRetriever { RequireHttps = false })
        {
            RefreshInterval = RefreshInterval,
            AutomaticRefreshInterval = TimeSpan.FromMinutes(10)
        };

        return new JwtTrust(issuer, audience, null, manager);
    }

    /// <summary>
    /// Local keys when this host is <paramref name="issuer"/>, otherwise the JWKS at
    /// <c>Jwt:TrustedJwksUrl</c> - which then has to be configured.
    /// </summary>
    public static JwtTrust Create(IConfiguration configuration, string issuer, string audience, LocalJwtIssuer? local)
    {
        if (local is not null && local.Issuer == issuer)
        {
            return Local(issuer, audience, local.Keys.PublishedKeys);
        }

        var url = configuration[TrustedJwksUrlConfig];
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new InvalidOperationException(
                $"{TrustedJwksUrlConfig} is not configured. This host accepts tokens issued by '{issuer}' and needs that issuer's /.well-known/jwks.json.");
        }

        return Remote(issuer, audience, url);
    }

    public TokenValidationParameters CreateParameters() => new()
    {
        ValidIssuer = Issuer,
        ValidAudience = Audience,
        ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256],
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        RequireSignedTokens = true,
        RequireExpirationTime = true,
        ClockSkew = ClockSkew,
        IssuerSigningKeys = _localKeys,
        // With a configuration manager attached the token handler resolves keys itself, and on an
        // unknown kid requests a refresh and retries - the rotation path described on the class.
        ConfigurationManager = ConfigurationManager
    };

    /// <summary>Validates outside the authentication pipeline - the socket host's upgrade request.</summary>
    /// <exception cref="SecurityTokenException">The token is not one this trust accepts.</exception>
    public async Task<ClaimsPrincipal> ValidateAsync(string token)
    {
        TokenValidationResult result = await _handler.ValidateTokenAsync(token, CreateParameters());
        if (!result.IsValid)
        {
            throw result.Exception as SecurityTokenException ?? new SecurityTokenException("Invalid token.", result.Exception);
        }

        return new ClaimsPrincipal(result.ClaimsIdentity);
    }

    /// <summary>
    /// Reads a bare JWKS into the configuration shape the IdentityModel stack consumes. There is no
    /// OpenID discovery document - the hosts publish keys, not a provider - so the issuer is the one
    /// this trust was created for rather than anything the remote end claims.
    /// </summary>
    private sealed class JwksRetriever(string issuer) : IConfigurationRetriever<OpenIdConnectConfiguration>
    {
        public async Task<OpenIdConnectConfiguration> GetConfigurationAsync(string address, IDocumentRetriever retriever, CancellationToken cancel)
        {
            string json = await retriever.GetDocumentAsync(address, cancel);
            var keys = new JsonWebKeySet(json);
            var configuration = new OpenIdConnectConfiguration { Issuer = issuer, JwksUri = address, JsonWebKeySet = keys };
            foreach (SecurityKey key in keys.GetSigningKeys())
            {
                configuration.SigningKeys.Add(key);
            }

            return configuration;
        }
    }
}

/// <summary>Which issuer this host signs as, when it signs at all.</summary>
public sealed record LocalJwtIssuer(string Issuer, JwtSigningKeys Keys);
