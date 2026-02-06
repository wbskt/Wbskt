using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Webskt.Auth.Host.Models;
using Webskt.Auth.Host.Providers;
using Webskt.Common.Abstraction.Exceptions;
using Webskt.Common.Security;

namespace Webskt.Auth.Host.Services;

internal class AuthService : IAuthService
{
    private readonly IAuthProvider _provider;
    private readonly IJwtService _jwtService;
    private readonly PasswordHasher<User> _passwordHasher = new();

    public AuthService(IAuthProvider provider, IJwtService jwtService)
    {
        _provider = provider;
        _jwtService = jwtService;
    }

    public async Task<LoginResponse> LoginAsync(string email, string password, string ipAddress)
    {
        var user = await _provider.GetByEmailAsync(email);

        var verificationResult = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);

        if (verificationResult == PasswordVerificationResult.Failed)
        {
            throw new SecurityException("Invalid credentials.");
        }

        if (verificationResult == PasswordVerificationResult.SuccessRehashNeeded)
        {
            // In a real scenario, we should update the hash in the DB here
            // user.PasswordHash = _passwordHasher.HashPassword(user, password);
            // await _provider.UpdateUserAsync(user);
        }

        if (!user.IsActive)
        {
            throw new SecurityException("User is inactive.");
        }

        var accessToken = GenerateAccessToken(user);
        var refreshToken = GenerateRefreshToken(user.Id);

        await _provider.InsertRefreshTokenAsync(refreshToken, ipAddress);

        return new LoginResponse(accessToken, refreshToken.Token);
    }

    public async Task<LoginResponse> RefreshTokenAsync(string token, string ipAddress)
    {
        var existingToken = await _provider.GetRefreshTokenAsync(token);

        if (!existingToken.IsActive)
        {
            throw new SecurityException("Token is no longer active.");
        }

        var user = await _provider.GetByIdAsync(existingToken.UserId);

        if (!user.IsActive)
        {
            throw new SecurityException("User is inactive.");
        }

        var newAccessToken = GenerateAccessToken(user);
        var newRefreshToken = GenerateRefreshToken(user.Id);

        // TODO: Publish TokenRotated event
        await _provider.InsertRefreshTokenAsync(newRefreshToken, ipAddress);

        return new LoginResponse(newAccessToken, newRefreshToken.Token);
    }

    public async Task<bool> VerifyPermissionAsync(int userId, string permissionSlug)
    {
        return await _provider.VerifyPermissionAsync(userId, permissionSlug);
    }

    public async Task<IReadOnlyCollection<PermissionResponse>> GetPermissionsAsync()
    {
        return await _provider.GetPermissionsAsync();
    }

    public async Task<IReadOnlyCollection<RoleResponse>> GetRolesAsync()
    {
        return await _provider.GetRolesAsync();
    }

    public async Task<IReadOnlyCollection<GroupResponse>> GetGroupsAsync()
    {
        return await _provider.GetGroupsAsync();
    }

    public async Task RegisterUserAsync(string username, string email, string password)
    {
        var user = new User 
        { 
            Username = username, 
            Email = email 
        };

        user.PasswordHash = _passwordHasher.HashPassword(user, password);

        await _provider.InsertUserAsync(user);
    }

    public async Task CreateRoleAsync(string name, string description)
    {
        await _provider.InsertRoleAsync(name, description);
    }

    public async Task CreateGroupAsync(string name, int? parentGroupId)
    {
        await _provider.InsertGroupAsync(name, parentGroupId);
    }

    public async Task AddUserToGroupAsync(int userId, int groupId)
    {
        await _provider.InsertUserGroupAsync(userId, groupId);
    }

    public async Task CreatePermissionAsync(string slug, string description)
    {
        await _provider.InsertPermissionAsync(slug, description);
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
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim("type", "user")
        };

        return _jwtService.GenerateToken(claims, TimeSpan.FromMinutes(15));
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