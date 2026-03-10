using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Host.Providers;
using Wbskt.Common.Abstraction.Models.Auth;
using Wbskt.Common.Security;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Auth;
using Wbskt.Foundation.Abstraction.Exceptions;

namespace Wbskt.Auth.Host.Services;

internal sealed class AuthService : IAuthService
{
    private readonly IAuthProvider _provider;
    private readonly IJwtService _jwtService;
    private readonly IEventBus _eventBus;
    private readonly PasswordHasher<User> _passwordHasher = new();

    public AuthService(IAuthProvider provider, IJwtService jwtService, IEventBus eventBus)
    {
        _provider = provider;
        _jwtService = jwtService;
        _eventBus = eventBus;
    }

    public async Task<LoginResponse> LoginAsync(string email, string password, string ipAddress, CancellationToken cancellationToken = default)
    {
        try 
        {
            var user = await _provider.GetByEmailAsync(email, cancellationToken);
            var verificationResult = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);

            if (verificationResult == PasswordVerificationResult.Failed)
            {
                await _eventBus.PublishAsync(new UserLoginFailedEvent(user.Id, ipAddress, "Invalid password"), cancellationToken);
                throw new SecurityException("Invalid credentials.");
            }

            if (!user.IsActive)
            {
                await _eventBus.PublishAsync(new UserLoginFailedEvent(user.Id, ipAddress, "User inactive"), cancellationToken);
                throw new SecurityException("User is inactive.");
            }

            var accessToken = GenerateAccessToken(user);
            var refreshToken = GenerateRefreshToken(user.Id);

            await _provider.InsertRefreshTokenAsync(refreshToken, ipAddress, cancellationToken);
            await _eventBus.PublishAsync(new UserLoginSuccessEvent(user.Id, ipAddress), cancellationToken);

            return new LoginResponse(accessToken, refreshToken.Token);
        }
        catch (SecurityException)
        {
            throw;
        }
        catch (Exception ex)
        {
            await _eventBus.PublishAsync(new SecurityAlertEvent(
                "LoginFailure", 
                $"Unexpected error during login for {email}: {ex.Message}", 
                ipAddress,
                $"Email: {email}"), cancellationToken);
            
            throw;
        }
    }

    public async Task<LoginResponse> RefreshTokenAsync(string token, string ipAddress, CancellationToken cancellationToken = default)
    {
        var existingToken = await _provider.GetRefreshTokenAsync(token, cancellationToken);

        if (!existingToken.IsActive)
        {
            throw new SecurityException("Token is no longer active.");
        }

        var user = await _provider.GetByIdAsync(existingToken.UserId, cancellationToken);

        if (!user.IsActive)
        {
            throw new SecurityException("User is inactive.");
        }

        var newAccessToken = GenerateAccessToken(user);
        var newRefreshToken = GenerateRefreshToken(user.Id);

        await _provider.InsertRefreshTokenAsync(newRefreshToken, ipAddress, cancellationToken);
        await _eventBus.PublishAsync(new TokenRotatedEvent(user.Id, ipAddress), cancellationToken);

        return new LoginResponse(newAccessToken, newRefreshToken.Token);
    }

    public async Task<bool> VerifyPermissionAsync(int userId, string permissionSlug, CancellationToken cancellationToken = default)
    {
        return await _provider.VerifyPermissionAsync(userId, permissionSlug, cancellationToken);
    }

    public async Task<IReadOnlyCollection<PermissionResponse>> GetPermissionsAsync(CancellationToken cancellationToken = default)
    {
        return await _provider.GetPermissionsAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<RoleResponse>> GetRolesAsync(CancellationToken cancellationToken = default)
    {
        return await _provider.GetRolesAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<GroupResponse>> GetGroupsAsync(CancellationToken cancellationToken = default)
    {
        return await _provider.GetGroupsAsync(cancellationToken);
    }

    public async Task RegisterUserAsync(string username, string email, string password, CancellationToken cancellationToken = default)
    {
        var user = new User 
        { 
            Username = username, 
            Email = email 
        };

        user.PasswordHash = _passwordHasher.HashPassword(user, password);

        var userId = await _provider.InsertUserAsync(user, cancellationToken);

        await _eventBus.PublishAsync(new UserRegisteredEvent(userId, username, email), cancellationToken);
    }

    public async Task CreateRoleAsync(string name, string description, CancellationToken cancellationToken = default)
    {
        await _provider.InsertRoleAsync(name, description, cancellationToken);
    }

    public async Task CreateGroupAsync(string name, int? parentGroupId, CancellationToken cancellationToken = default)
    {
        await _provider.InsertGroupAsync(name, parentGroupId, cancellationToken);
    }

    public async Task AddUserToGroupAsync(int userId, int groupId, CancellationToken cancellationToken = default)
    {
        await _provider.InsertUserGroupAsync(userId, groupId, cancellationToken);
    }

    public async Task CreatePermissionAsync(string slug, string description, CancellationToken cancellationToken = default)
    {
        await _provider.InsertPermissionAsync(slug, description, cancellationToken);
    }

    public async Task GrantRolePermissionAsync(int roleId, string permissionSlug, bool isDeny, CancellationToken cancellationToken = default)
    {
        await _provider.GrantRolePermissionAsync(roleId, permissionSlug, isDeny, cancellationToken);
        
        await _eventBus.PublishAsync(new RolePermissionsChangedEvent(roleId), cancellationToken);
    }

    public async Task GrantUserPermissionAsync(int userId, string permissionSlug, bool isDeny, CancellationToken cancellationToken = default)
    {
        await _provider.GrantUserPermissionAsync(userId, permissionSlug, isDeny, cancellationToken);
        
        await _eventBus.PublishAsync(new UserPermissionsChangedEvent(userId), cancellationToken);
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