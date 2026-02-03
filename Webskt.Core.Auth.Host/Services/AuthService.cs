using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Tokens;
using Webskt.Core.Auth.Host.Models;
using Webskt.Core.Auth.Host.Providers;

namespace Webskt.Core.Auth.Host.Services;

public class AuthService : IAuthService
{
    private readonly IAuthProvider _provider;
    private readonly IConfiguration _configuration;

    public AuthService(IAuthProvider provider, IConfiguration configuration)
    {
        _provider = provider;
        _configuration = configuration;
    }

    public async Task<LoginResponse> LoginAsync(string email, string password, string ipAddress)
    {
        var user = await _provider.GetUserByEmailAsync(email);
        if (user == null || !BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
        {
            throw new UnauthorizedAccessException("Invalid credentials.");
        }

        if (!user.IsActive) throw new UnauthorizedAccessException("User is inactive.");

        var accessToken = GenerateAccessToken(user);
        var refreshToken = GenerateRefreshToken(user.Id);

        await _provider.SaveRefreshTokenAsync(refreshToken, ipAddress);

        return new LoginResponse(accessToken, refreshToken.Token);
    }

    public async Task<LoginResponse> RefreshTokenAsync(string token, string ipAddress)
    {
        var existingToken = await _provider.GetRefreshTokenAsync(token);
        if (existingToken == null || !existingToken.IsActive)
        {
            throw new UnauthorizedAccessException("Invalid token.");
        }
        
        // Revoke old token? Or just Rotate? For now, we just issue new.
        // Ideally: Revoke existingToken.

        var user = await _provider.GetUserByIdAsync(existingToken.UserId);
        if (user == null || !user.IsActive) throw new UnauthorizedAccessException("User invalid.");

        var newAccessToken = GenerateAccessToken(user);
        var newRefreshToken = GenerateRefreshToken(user.Id);
        
        // In a real app, revoke the old one here
        // await _provider.RevokeToken(existingToken.Id);
        // And chain them via ReplacedByToken

        await _provider.SaveRefreshTokenAsync(newRefreshToken, ipAddress);

        return new LoginResponse(newAccessToken, newRefreshToken.Token);
    }

    public async Task<bool> ValidatePermissionAsync(int userId, string permissionSlug)
    {
        return await _provider.CheckPermissionAsync(userId, permissionSlug);
    }

    public async Task RegisterUserAsync(string username, string email, string password)
    {
        var hash = BCrypt.Net.BCrypt.HashPassword(password);
        var user = new User { Username = username, Email = email, PasswordHash = hash };
        await _provider.CreateUserAsync(user);
    }

    public async Task CreateRoleAsync(string name, string description)
    {
        await _provider.CreateRoleAsync(name, description);
    }

    public async Task CreateGroupAsync(string name, int? parentGroupId)
    {
        await _provider.CreateGroupAsync(name, parentGroupId);
    }

    public async Task AddUserToGroupAsync(int userId, int groupId)
    {
        await _provider.AddUserToGroupAsync(userId, groupId);
    }

    public async Task CreatePermissionAsync(string slug, string description)
    {
        await _provider.CreatePermissionAsync(slug, description);
    }

    public async Task GrantRolePermissionAsync(int roleId, string permissionSlug, bool isDeny)
    {
        await _provider.GrantRolePermissionAsync(roleId, permissionSlug, isDeny);
        // TODO: Publish RolePermissionsChanged event
    }

    public async Task GrantUserPermissionAsync(int userId, string permissionSlug, bool isDeny)
    {
        await _provider.GrantUserPermissionAsync(userId, permissionSlug, isDeny);
        // TODO: Publish UserPermissionsChanged event
    }

    private string GenerateAccessToken(User user)
    {
        var key = Encoding.ASCII.GetBytes(_configuration["Jwt:Key"]!);
        var tokenHandler = new JwtSecurityTokenHandler();
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.Email, user.Email)
            }),
            Expires = DateTime.UtcNow.AddMinutes(15),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };
        var token = tokenHandler.CreateToken(descriptor);
        return tokenHandler.WriteToken(token);
    }

    private RefreshToken GenerateRefreshToken(int userId)
    {
        using var rng = RandomNumberGenerator.Create();
        var randomBytes = new byte[64];
        rng.GetBytes(randomBytes);
        return new RefreshToken
        {
            UserId = userId,
            Token = Convert.ToBase64String(randomBytes),
            Expires = DateTime.UtcNow.AddDays(7),
        };
    }
}
