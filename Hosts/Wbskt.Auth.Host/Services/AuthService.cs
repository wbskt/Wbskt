using System.Diagnostics;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Host.Providers;
using Wbskt.Auth.Host.Telemetry;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Auth;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Security;
using Wbskt.Primitives.Exceptions;

namespace Wbskt.Auth.Host.Services;

internal sealed class AuthService : IAuthService
{
    private const int DefaultTenantId = 1;

    private readonly IAuthProvider _provider;
    private readonly IJwtService _jwtService;
    private readonly IEventBus _eventBus;
    private readonly ILogger<AuthService> _logger;
    private readonly AuthMetrics _metrics;
    private readonly PasswordHasher<User> _passwordHasher = new();

    public AuthService(
        IAuthProvider provider, 
        IJwtService jwtService, 
        IEventBus eventBus,
        ILogger<AuthService> logger,
        AuthMetrics metrics)
    {
        _provider = provider;
        _jwtService = jwtService;
        _eventBus = eventBus;
        _logger = logger;
        _metrics = metrics;
    }

    public async Task<Result<LoginResponse>> LoginAsync(string email, string password, string ipAddress, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Attempting login for email: {Email} from IP: {IpAddress}", email, ipAddress);

        try 
        {
            User user;
            try
            {
                user = await _provider.GetByEmailAsync(email, cancellationToken);
                _logger.LogDebug("User record resolved for email: {Email}", email);
            }
            catch (SecurityException ex)
            {
                _logger.LogWarning("Login failed: User with email {Email} not found. IP: {IpAddress}. Error: {Message}", email, ipAddress, ex.Message);
                _logger.LogTrace(ex, "Login user lookup failed stack trace for {Email}", email);
                _metrics.RecordLogin("invalid_credentials");
                await _eventBus.PublishAsync(new UserLoginFailedEvent(-1, Guid.Empty, ipAddress, "Invalid credentials"), cancellationToken);
                return Result<LoginResponse>.Failure(Error.Unauthorized("AUTH_INVALID_CREDENTIALS", "Invalid credentials."));
            }

            var stopwatch = Stopwatch.StartNew();
            var verificationResult = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
            stopwatch.Stop();
            _metrics.RecordPasswordHash(stopwatch.Elapsed.TotalMilliseconds);

            if (verificationResult == PasswordVerificationResult.Failed)
            {
                _logger.LogWarning("Login failed: Invalid password for email {Email}. IP: {IpAddress}", email, ipAddress);
                _metrics.RecordLogin("invalid_credentials");
                await _eventBus.PublishAsync(new UserLoginFailedEvent(user.Id, user.RefId, ipAddress, "Invalid password"), cancellationToken);
                return Result<LoginResponse>.Failure(Error.Unauthorized("AUTH_INVALID_CREDENTIALS", "Invalid credentials."));
            }

            if (!user.IsActive)
            {
                _logger.LogWarning("Login failed: Account is inactive for user {Username} ({Email}). IP: {IpAddress}", user.Username, email, ipAddress);
                _metrics.RecordLogin("user_inactive");
                await _eventBus.PublishAsync(new UserLoginFailedEvent(user.Id, user.RefId, ipAddress, "User inactive"), cancellationToken);
                return Result<LoginResponse>.Failure(Error.Unauthorized("AUTH_USER_INACTIVE", "User is inactive."));
            }

            _logger.LogDebug("Credentials verified successfully for user: {Username} ({Email}). Generating tokens...", user.Username, email);
            var accessToken = GenerateAccessToken(user);
            var refreshToken = GenerateRefreshToken(user.Id);

            await _provider.InsertRefreshTokenAsync(refreshToken, ipAddress, cancellationToken);
            _logger.LogDebug("Refresh token inserted for user ID: {UserId}", user.Id);

            await _eventBus.PublishAsync(new UserLoginSuccessEvent(user.Id, user.RefId, ipAddress), cancellationToken);
            _logger.LogInformation("User {Username} logged in successfully. IP: {IpAddress}", user.Username, ipAddress);

            _metrics.RecordLogin("success");
            return Result<LoginResponse>.Success(new LoginResponse(accessToken, refreshToken.Token));
        }
        catch (Exception ex)
        {
            _logger.LogError("Unexpected error during login for email: {Email}. IP: {IpAddress}. Error: {Message}", email, ipAddress, ex.Message);
            _logger.LogTrace(ex, "Login exception stack trace for {Email}", email);
            _metrics.RecordLogin("error");
            await _eventBus.PublishAsync(new SecurityAlertEvent(
                "LoginFailure", 
                $"Unexpected error during login for {email}: {ex.Message}", 
                ipAddress,
                $"Email: {email}"), cancellationToken);
            
            return Result<LoginResponse>.Failure(Error.Failure("AUTH_LOGIN_ERROR", ex.Message));
        }
    }

    public async Task<Result<LoginResponse>> RefreshTokenAsync(string token, string ipAddress, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Attempting token refresh from IP: {IpAddress}", ipAddress);

        try
        {
            RefreshToken existingToken;
            try
            {
                existingToken = await _provider.GetRefreshTokenAsync(token, cancellationToken);
            }
            catch (SecurityException ex)
            {
                _logger.LogWarning("Token refresh failed: Provided token is invalid. IP: {IpAddress}. Error: {Message}", ipAddress, ex.Message);
                _logger.LogTrace(ex, "Token refresh lookup failed stack trace");
                _metrics.RecordRefresh("invalid_token");
                return Result<LoginResponse>.Failure(Error.Unauthorized("AUTH_INVALID_TOKEN", "Invalid refresh token."));
            }

            // A token we already retired is being presented again. The legitimate client moved on to
            // its replacement, so whoever sent this either kept a copy or intercepted one. Treat the
            // whole session family as compromised rather than just refusing this one request.
            if (existingToken.Revoked is not null)
            {
                _logger.LogWarning("Token refresh failed: Replay of a revoked token for user ID {UserId}. Revoking all sessions. IP: {IpAddress}", existingToken.UserId, ipAddress);
                await _provider.RevokeAllRefreshTokensForUserAsync(existingToken.UserId, ipAddress, cancellationToken);
                _metrics.RecordRefresh("token_replayed");

                await _eventBus.PublishAsync(new SecurityAlertEvent(
                    "RefreshTokenReplay",
                    $"A revoked refresh token was replayed for user ID {existingToken.UserId}; all sessions revoked.",
                    ipAddress,
                    $"UserId: {existingToken.UserId}"), cancellationToken);

                return Result<LoginResponse>.Failure(Error.Unauthorized("AUTH_TOKEN_INACTIVE", "Token is no longer active."));
            }

            if (!existingToken.IsActive)
            {
                _logger.LogWarning("Token refresh failed: Provided token for user ID {UserId} has expired. IP: {IpAddress}", existingToken.UserId, ipAddress);
                _metrics.RecordRefresh("token_inactive");
                return Result<LoginResponse>.Failure(Error.Unauthorized("AUTH_TOKEN_INACTIVE", "Token is no longer active."));
            }

            User user;
            try
            {
                user = await _provider.GetByIdAsync(existingToken.UserId, cancellationToken);
                _logger.LogDebug("User record resolved for ID: {UserId}", existingToken.UserId);
            }
            catch (SecurityException ex)
            {
                _logger.LogWarning("Token refresh failed: User with ID {UserId} not found. IP: {IpAddress}. Error: {Message}", existingToken.UserId, ipAddress, ex.Message);
                _logger.LogTrace(ex, "Token refresh user lookup failed stack trace for User ID {UserId}", existingToken.UserId);
                _metrics.RecordRefresh("user_not_found");
                return Result<LoginResponse>.Failure(Error.Unauthorized("AUTH_USER_NOT_FOUND", "User not found."));
            }

            if (!user.IsActive)
            {
                _logger.LogWarning("Token refresh failed: Account is inactive for user {Username}. IP: {IpAddress}", user.Username, ipAddress);
                _metrics.RecordRefresh("user_inactive");
                return Result<LoginResponse>.Failure(Error.Unauthorized("AUTH_USER_INACTIVE", "User is inactive."));
            }

            _logger.LogDebug("Rotating refresh token for user: {Username}", user.Username);
            var newAccessToken = GenerateAccessToken(user);
            var newRefreshToken = GenerateRefreshToken(user.Id);

            await _provider.InsertRefreshTokenAsync(newRefreshToken, ipAddress, cancellationToken);

            // Retire the presented token now that its replacement exists. Without this the old token
            // stays usable for its full lifetime, so a leaked one could be replayed indefinitely
            // alongside the real session.
            await _provider.RevokeRefreshTokenAsync(token, ipAddress, newRefreshToken.Token, cancellationToken);

            await _eventBus.PublishAsync(new TokenRotatedEvent(user.Id, user.RefId, ipAddress), cancellationToken);

            _logger.LogInformation("Token refreshed successfully for user: {Username}. IP: {IpAddress}", user.Username, ipAddress);
            _metrics.RecordRefresh("success");
            return Result<LoginResponse>.Success(new LoginResponse(newAccessToken, newRefreshToken.Token));
        }
        catch (Exception ex)
        {
            _logger.LogError("Unexpected error during token refresh from IP: {IpAddress}. Error: {Message}", ipAddress, ex.Message);
            _logger.LogTrace(ex, "Token refresh exception stack trace");
            _metrics.RecordRefresh("error");
            return Result<LoginResponse>.Failure(Error.Failure("AUTH_REFRESH_ERROR", ex.Message));
        }
    }

    public async Task<Result> LogoutAsync(string token, string ipAddress, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Attempting logout from IP: {IpAddress}", ipAddress);

        try
        {
            // Deliberately not reporting whether the token existed: logout is unauthenticated by
            // necessity, and a distinguishable response would turn it into a token oracle.
            var revoked = await _provider.RevokeRefreshTokenAsync(token, ipAddress, null, cancellationToken);
            _logger.LogInformation("Logout revoked {RevokedCount} refresh token(s). IP: {IpAddress}", revoked, ipAddress);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError("Unexpected error during logout from IP: {IpAddress}. Error: {Message}", ipAddress, ex.Message);
            _logger.LogTrace(ex, "Logout exception stack trace");
            return Result.Failure(Error.Failure("AUTH_LOGOUT_ERROR", ex.Message));
        }
    }

    public async Task<Result> LogoutAllAsync(int userId, string ipAddress, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Attempting to revoke all sessions for user ID: {UserId} from IP: {IpAddress}", userId, ipAddress);

        try
        {
            var revoked = await _provider.RevokeAllRefreshTokensForUserAsync(userId, ipAddress, cancellationToken);
            _logger.LogInformation("Revoked {RevokedCount} refresh token(s) for user ID: {UserId}", revoked, userId);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError("Unexpected error revoking sessions for user ID: {UserId}. Error: {Message}", userId, ex.Message);
            _logger.LogTrace(ex, "LogoutAll exception stack trace for user {UserId}", userId);
            return Result.Failure(Error.Failure("AUTH_LOGOUT_ERROR", ex.Message));
        }
    }

    public async Task<Result> SetUserActiveAsync(int userId, bool isActive, string ipAddress, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Setting IsActive={IsActive} for user ID: {UserId}", isActive, userId);

        try
        {
            var user = await _provider.GetByIdAsync(userId, cancellationToken);
            await _provider.SetUserActiveAsync(userId, isActive, cancellationToken);

            // The access token stays valid until it expires, so deactivation only fully takes hold
            // once the refresh tokens are gone and the current access token lapses.
            if (!isActive)
            {
                var revoked = await _provider.RevokeAllRefreshTokensForUserAsync(userId, ipAddress, cancellationToken);
                _logger.LogInformation("Deactivated user ID {UserId} and revoked {RevokedCount} refresh token(s)", userId, revoked);

                await _eventBus.PublishAsync(new SecurityAlertEvent(
                    "UserDeactivated",
                    $"User ID {userId} ({user.Username}) was deactivated and all sessions revoked.",
                    ipAddress,
                    $"UserId: {userId}"), cancellationToken);
            }

            return Result.Success();
        }
        catch (SecurityException ex)
        {
            _logger.LogWarning("Cannot set active state: user ID {UserId} not found. Error: {Message}", userId, ex.Message);
            return Result.Failure(Error.NotFound("AUTH_USER_NOT_FOUND", "User not found."));
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to set active state for user ID: {UserId}. Error: {Message}", userId, ex.Message);
            _logger.LogTrace(ex, "SetUserActive failure stack trace for user {UserId}", userId);
            return Result.Failure(Error.Failure("AUTH_USER_STATE_ERROR", ex.Message));
        }
    }

    public async Task<Result<bool>> VerifyPermissionAsync(int userId, int tenantId, int? workspaceId, string permissionSlug, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Verifying permission '{PermissionSlug}' for user ID: {UserId} (Tenant: {TenantId}, Workspace: {WorkspaceId})", permissionSlug, userId, tenantId, workspaceId);

        try
        {
            var isAllowed = await _provider.VerifyPermissionAsync(userId, tenantId, workspaceId, permissionSlug, cancellationToken);
            _logger.LogTrace("Permission '{PermissionSlug}' verification result for user ID {UserId}: {IsAllowed}", permissionSlug, userId, isAllowed);
            _metrics.RecordPermissionCheck(permissionSlug, isAllowed ? "allowed" : "denied");
            return Result<bool>.Success(isAllowed);
        }
        catch (Exception ex)
        {
            _logger.LogError("Error verifying permission '{PermissionSlug}' for user ID: {UserId}. Error: {Message}", permissionSlug, userId, ex.Message);
            _logger.LogTrace(ex, "VerifyPermission stack trace for user {UserId}", userId);
            _metrics.RecordPermissionCheck(permissionSlug, "error");
            return Result<bool>.Failure(Error.Failure("AUTH_PERMISSION_ERROR", ex.Message));
        }
    }

    public async Task<Result<IReadOnlyCollection<TenantResponse>>> GetTenantsForUserAsync(int userId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Querying tenants for user ID: {UserId}", userId);

        try
        {
            var tenants = await _provider.GetTenantsForUserAsync(userId, cancellationToken);
            _logger.LogTrace("Retrieved {Count} tenants for user ID: {UserId}", tenants.Count, userId);
            var items = tenants.Select(t => new TenantResponse(t.Id, t.RefId, t.Name)).ToList() as IReadOnlyCollection<TenantResponse>;
            return Result<IReadOnlyCollection<TenantResponse>>.Success(items);
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to query tenants for user ID: {UserId}. Error: {Message}", userId, ex.Message);
            _logger.LogTrace(ex, "GetTenantsForUser stack trace for user {UserId}", userId);
            return Result<IReadOnlyCollection<TenantResponse>>.Failure(Error.Failure("AUTH_QUERY_ERROR", ex.Message));
        }
    }

    public async Task<Result<IReadOnlyCollection<PermissionResponse>>> GetPermissionsAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Querying all system permissions");

        try
        {
            var permissions = await _provider.GetPermissionsAsync(cancellationToken);
            _logger.LogTrace("Retrieved {Count} permissions from database", permissions.Count);
            return Result<IReadOnlyCollection<PermissionResponse>>.Success(permissions);
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to query system permissions. Error: {Message}", ex.Message);
            _logger.LogTrace(ex, "Query permissions stack trace");
            return Result<IReadOnlyCollection<PermissionResponse>>.Failure(Error.Failure("AUTH_QUERY_ERROR", ex.Message));
        }
    }

    public async Task<Result<IReadOnlyCollection<RoleResponse>>> GetRolesAsync(int tenantId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Querying all roles for tenant ID: {TenantId}", tenantId);

        try
        {
            var roles = await _provider.GetRolesAsync(tenantId, cancellationToken);
            _logger.LogTrace("Retrieved {Count} roles from database", roles.Count);
            return Result<IReadOnlyCollection<RoleResponse>>.Success(roles);
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to query system roles. Error: {Message}", ex.Message);
            _logger.LogTrace(ex, "Query roles stack trace");
            return Result<IReadOnlyCollection<RoleResponse>>.Failure(Error.Failure("AUTH_QUERY_ERROR", ex.Message));
        }
    }

    public async Task<Result<IReadOnlyCollection<GroupResponse>>> GetGroupsAsync(int tenantId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Querying all user groups for tenant ID: {TenantId}", tenantId);

        try
        {
            var groups = await _provider.GetGroupsAsync(tenantId, cancellationToken);
            _logger.LogTrace("Retrieved {Count} user groups from database", groups.Count);
            return Result<IReadOnlyCollection<GroupResponse>>.Success(groups);
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to query system user groups. Error: {Message}", ex.Message);
            _logger.LogTrace(ex, "Query groups stack trace");
            return Result<IReadOnlyCollection<GroupResponse>>.Failure(Error.Failure("AUTH_QUERY_ERROR", ex.Message));
        }
    }

    public async Task<Result> RegisterUserAsync(string username, string email, string password, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Attempting to register user: {Username} with email: {Email}", username, email);

        try
        {
            var user = new User 
            { 
                Username = username, 
                Email = email 
            };

            user.PasswordHash = _passwordHasher.HashPassword(user, password);

            var userId = await _provider.InsertUserAsync(user, cancellationToken);
            _logger.LogDebug("User record inserted with database ID: {UserId}", userId);

            // TODO(arch): multi-tenant sign-up flow. For now every new user joins the default tenant.
            await _provider.InsertTenantMemberAsync(DefaultTenantId, userId, cancellationToken);

            user = await _provider.GetByIdAsync(userId, cancellationToken);

            await _eventBus.PublishAsync(new UserRegisteredEvent(userId, user.RefId, username, email), cancellationToken);
            _logger.LogInformation("User {Username} registered successfully. RefId: {RefId}", username, user.RefId);
            
            _metrics.RecordRegistration("success");
            return Result.Success();
        }
        catch (Exception ex)
        {
            if (ex is SqlException { Number: 2601 or 2627 })
            {
                _logger.LogWarning("User registration failed: Conflict on Username={Username} or Email={Email}. Error: {Message}", username, email, ex.Message);
                _logger.LogTrace(ex, "User registration conflict stack trace for {Username}", username);
                _metrics.RecordRegistration("conflict");
                return Result.Failure(Error.Conflict("AUTH_USER_CONFLICT", "Username or email is already registered."));
            }
            _logger.LogError("Failed to register user: {Username} ({Email}). Error: {Message}", username, email, ex.Message);
            _logger.LogTrace(ex, "User registration failure stack trace for {Username}", username);
            _metrics.RecordRegistration("error");
            return Result.Failure(Error.Failure("AUTH_REGISTRATION_ERROR", ex.Message));
        }
    }

    public async Task<Result> CreateRoleAsync(string name, string description, int tenantId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating role: {RoleName} in tenant ID: {TenantId}", name, tenantId);

        try
        {
            await _provider.InsertRoleAsync(name, description, tenantId, cancellationToken);
            _logger.LogInformation("Role {RoleName} created successfully", name);
            return Result.Success();
        }
        catch (Exception ex)
        {
            if (ex is SqlException { Number: 2601 or 2627 })
            {
                _logger.LogWarning("Role creation failed: Conflict on name={RoleName}. Error: {Message}", name, ex.Message);
                _logger.LogTrace(ex, "Role creation conflict stack trace for {RoleName}", name);
                return Result.Failure(Error.Conflict("AUTH_ROLE_CONFLICT", "Role already exists."));
            }
            _logger.LogError("Failed to create role: {RoleName}. Error: {Message}", name, ex.Message);
            _logger.LogTrace(ex, "Role creation failure stack trace for {RoleName}", name);
            return Result.Failure(Error.Failure("AUTH_ROLE_ERROR", ex.Message));
        }
    }

    public async Task<Result> CreateGroupAsync(string name, int? parentGroupId, int tenantId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating group: {GroupName} (Parent ID: {ParentGroupId}) in tenant ID: {TenantId}", name, parentGroupId, tenantId);

        try
        {
            await _provider.InsertGroupAsync(name, parentGroupId, tenantId, cancellationToken);
            _logger.LogInformation("Group {GroupName} created successfully", name);
            return Result.Success();
        }
        catch (Exception ex)
        {
            if (ex is SqlException { Number: 2601 or 2627 })
            {
                _logger.LogWarning("Group creation failed: Conflict on name={GroupName}. Error: {Message}", name, ex.Message);
                _logger.LogTrace(ex, "Group creation conflict stack trace for {GroupName}", name);
                return Result.Failure(Error.Conflict("AUTH_GROUP_CONFLICT", "Group already exists."));
            }
            _logger.LogError("Failed to create group: {GroupName}. Error: {Message}", name, ex.Message);
            _logger.LogTrace(ex, "Group creation failure stack trace for {GroupName}", name);
            return Result.Failure(Error.Failure("AUTH_GROUP_ERROR", ex.Message));
        }
    }

    public async Task<Result> AddUserToGroupAsync(int userId, int groupId, int tenantId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Adding user ID {UserId} to group ID {GroupId} (Tenant: {TenantId})", userId, groupId, tenantId);

        try
        {
            await _provider.InsertUserGroupAsync(userId, groupId, tenantId, cancellationToken);
            _logger.LogInformation("User ID {UserId} successfully added to group ID {GroupId}", userId, groupId);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to add user ID {UserId} to group ID {GroupId}. Error: {Message}", userId, groupId, ex.Message);
            _logger.LogTrace(ex, "AddUserToGroup failure stack trace for UserId {UserId}, GroupId {GroupId}", userId, groupId);
            return Result.Failure(Error.Failure("AUTH_USER_GROUP_ERROR", ex.Message));
        }
    }

    public async Task<Result> GrantRolePermissionAsync(int roleId, string permissionSlug, bool isDeny, int tenantId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Granting permission '{PermissionSlug}' to role ID {RoleId} (IsDeny: {IsDeny}, Tenant: {TenantId})", permissionSlug, roleId, isDeny, tenantId);

        try
        {
            await _provider.GrantRolePermissionAsync(roleId, permissionSlug, isDeny, tenantId, cancellationToken);
            await _eventBus.PublishAsync(new RolePermissionsChangedEvent(roleId), cancellationToken);
            _logger.LogInformation("Permission '{PermissionSlug}' successfully configured for role ID {RoleId}", permissionSlug, roleId);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to configure permission '{PermissionSlug}' for role ID {RoleId}. Error: {Message}", permissionSlug, roleId, ex.Message);
            _logger.LogTrace(ex, "GrantRolePermission failure stack trace for RoleId {RoleId}, Permission {PermissionSlug}", roleId, permissionSlug);
            return Result.Failure(Error.Failure("AUTH_GRANT_ERROR", ex.Message));
        }
    }

    public async Task<Result> GrantUserPermissionAsync(int userId, string permissionSlug, bool isDeny, int tenantId, int? workspaceId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Granting permission '{PermissionSlug}' directly to user ID {UserId} (IsDeny: {IsDeny}, Tenant: {TenantId}, Workspace: {WorkspaceId})", permissionSlug, userId, isDeny, tenantId, workspaceId);

        try
        {
            await _provider.GrantUserPermissionAsync(userId, permissionSlug, isDeny, tenantId, workspaceId, cancellationToken);
            var user = await _provider.GetByIdAsync(userId, cancellationToken);
            await _eventBus.PublishAsync(new UserPermissionsChangedEvent(userId, user.RefId), cancellationToken);
            _logger.LogInformation("Permission '{PermissionSlug}' successfully configured directly for user ID {UserId}", permissionSlug, userId);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to configure permission '{PermissionSlug}' directly for user ID {UserId}. Error: {Message}", permissionSlug, userId, ex.Message);
            _logger.LogTrace(ex, "GrantUserPermission failure stack trace for UserId {UserId}, Permission {PermissionSlug}", userId, permissionSlug);
            return Result.Failure(Error.Failure("AUTH_GRANT_ERROR", ex.Message));
        }
    }

    public async Task<Result> AssignUserRoleAsync(int userId, int roleId, int tenantId, int? workspaceId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Assigning role ID {RoleId} to user ID {UserId} (Tenant: {TenantId}, Workspace: {WorkspaceId})", roleId, userId, tenantId, workspaceId);

        try
        {
            await _provider.AssignUserRoleAsync(userId, roleId, tenantId, workspaceId, cancellationToken);
            var user = await _provider.GetByIdAsync(userId, cancellationToken);
            await _eventBus.PublishAsync(new UserPermissionsChangedEvent(userId, user.RefId), cancellationToken);
            _logger.LogInformation("Role ID {RoleId} successfully assigned to user ID {UserId}", roleId, userId);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to assign role ID {RoleId} to user ID {UserId}. Error: {Message}", roleId, userId, ex.Message);
            _logger.LogTrace(ex, "AssignUserRole failure stack trace for UserId {UserId}, RoleId {RoleId}", userId, roleId);
            return Result.Failure(Error.Failure("AUTH_ROLE_ASSIGN_ERROR", ex.Message));
        }
    }

    public async Task<Result> RemoveUserRoleAsync(int userId, int roleId, int tenantId, int? workspaceId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Removing role ID {RoleId} from user ID {UserId} (Tenant: {TenantId}, Workspace: {WorkspaceId})", roleId, userId, tenantId, workspaceId);

        try
        {
            await _provider.RemoveUserRoleAsync(userId, roleId, tenantId, workspaceId, cancellationToken);
            var user = await _provider.GetByIdAsync(userId, cancellationToken);
            await _eventBus.PublishAsync(new UserPermissionsChangedEvent(userId, user.RefId), cancellationToken);
            _logger.LogInformation("Role ID {RoleId} successfully removed from user ID {UserId}", roleId, userId);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to remove role ID {RoleId} from user ID {UserId}. Error: {Message}", roleId, userId, ex.Message);
            _logger.LogTrace(ex, "RemoveUserRole failure stack trace for UserId {UserId}, RoleId {RoleId}", userId, roleId);
            return Result.Failure(Error.Failure("AUTH_ROLE_ASSIGN_ERROR", ex.Message));
        }
    }

    public async Task<Result> AssignGroupRoleAsync(int groupId, int roleId, int tenantId, int? workspaceId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Assigning role ID {RoleId} to group ID {GroupId} (Tenant: {TenantId}, Workspace: {WorkspaceId})", roleId, groupId, tenantId, workspaceId);

        try
        {
            await _provider.AssignGroupRoleAsync(groupId, roleId, tenantId, workspaceId, cancellationToken);
            await _eventBus.PublishAsync(new RolePermissionsChangedEvent(roleId), cancellationToken);
            _logger.LogInformation("Role ID {RoleId} successfully assigned to group ID {GroupId}", roleId, groupId);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to assign role ID {RoleId} to group ID {GroupId}. Error: {Message}", roleId, groupId, ex.Message);
            _logger.LogTrace(ex, "AssignGroupRole failure stack trace for GroupId {GroupId}, RoleId {RoleId}", groupId, roleId);
            return Result.Failure(Error.Failure("AUTH_ROLE_ASSIGN_ERROR", ex.Message));
        }
    }

    public async Task<Result> RemoveGroupRoleAsync(int groupId, int roleId, int tenantId, int? workspaceId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Removing role ID {RoleId} from group ID {GroupId} (Tenant: {TenantId}, Workspace: {WorkspaceId})", roleId, groupId, tenantId, workspaceId);

        try
        {
            await _provider.RemoveGroupRoleAsync(groupId, roleId, tenantId, workspaceId, cancellationToken);
            await _eventBus.PublishAsync(new RolePermissionsChangedEvent(roleId), cancellationToken);
            _logger.LogInformation("Role ID {RoleId} successfully removed from group ID {GroupId}", roleId, groupId);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to remove role ID {RoleId} from group ID {GroupId}. Error: {Message}", roleId, groupId, ex.Message);
            _logger.LogTrace(ex, "RemoveGroupRole failure stack trace for GroupId {GroupId}, RoleId {RoleId}", groupId, roleId);
            return Result.Failure(Error.Failure("AUTH_ROLE_ASSIGN_ERROR", ex.Message));
        }
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

        return _jwtService.GenerateToken(claims, TimeSpan.FromMinutes(60));
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