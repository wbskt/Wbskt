using System.Security.Claims;

namespace Webskt.Common.Security;

public interface IJwtService
{
    string GenerateToken(IEnumerable<Claim> claims, TimeSpan expiresIn);
    Task<ClaimsPrincipal> ValidateToken(string token);
}
