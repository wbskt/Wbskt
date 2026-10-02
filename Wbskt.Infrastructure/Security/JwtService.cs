using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Wbskt.Infrastructure.Security;

public sealed class JwtService : IJwtService
{
    private readonly JwtSigningKeys _keys;
    private readonly string _issuer;
    private readonly JsonWebTokenHandler _handler = new();

    public JwtService(JwtSigningKeys keys, string issuer)
    {
        _keys = keys;
        _issuer = issuer;
    }

    public string GenerateToken(IEnumerable<Claim> claims, string audience, TimeSpan expiresIn)
    {
        var now = DateTime.UtcNow;
        var identity = new ClaimsIdentity(claims);

        // A unique id per token, so a token can be told apart from another minted in the same second
        // for the same subject - in logs, and by anything that later needs to revoke one token.
        identity.AddClaim(new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")));

        return _handler.CreateToken(new SecurityTokenDescriptor
        {
            Subject = identity,
            Issuer = _issuer,
            Audience = audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.Add(expiresIn),
            SigningCredentials = _keys.SigningCredentials
        });
    }
}
