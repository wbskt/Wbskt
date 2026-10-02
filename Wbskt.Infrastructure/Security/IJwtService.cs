using System.Security.Claims;

namespace Wbskt.Infrastructure.Security;

/// <summary>Mints tokens as this host's issuer. Validation lives in <see cref="JwtTrust"/>.</summary>
public interface IJwtService
{
    /// <param name="audience">One of <see cref="JwtAudiences"/>: the surface the token is good for.</param>
    string GenerateToken(IEnumerable<Claim> claims, string audience, TimeSpan expiresIn);
}
