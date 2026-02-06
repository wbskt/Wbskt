using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Webskt.Common.Security;

public class JwtService : IJwtService
{
    private readonly IConfiguration _configuration;

    public JwtService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string GenerateToken(IEnumerable<Claim> claims, TimeSpan expiresIn)
    {
        var keyString = _configuration["Jwt:Key"];
        
        // Logic requiring an empty line before this comment
        if (string.IsNullOrEmpty(keyString))
        {
            throw new InvalidOperationException("JWT Key is not configured.");
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(keyString));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var handler = new JsonWebTokenHandler();
        
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.Add(expiresIn),
            SigningCredentials = credentials
        };

        return handler.CreateToken(descriptor);
    }

    public async Task<ClaimsPrincipal> ValidateToken(string token)
    {
        var keyString = _configuration["Jwt:Key"];

        if (string.IsNullOrEmpty(keyString))
        {
            throw new InvalidOperationException("JWT Key is not configured.");
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(keyString));
        var handler = new JsonWebTokenHandler();

        var result = await handler.ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = key,
            ValidateIssuer = false,
            ValidateAudience = false,
            ClockSkew = TimeSpan.Zero
        });

        if (!result.IsValid)
        {
            throw new SecurityTokenException("Invalid token.");
        }

        return new ClaimsPrincipal(result.ClaimsIdentity);
    }
}
