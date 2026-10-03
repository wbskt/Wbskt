using Wbskt.Auth.Host.Models;
using Wbskt.Models;
using Wbskt.Primitives;

namespace Wbskt.Auth.Host.Providers;

internal interface IAuthProvider : IReferenceProvider
{
    Task<User> GetByEmailAsync(string email, CancellationToken cancellationToken = default);
    Task<User> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<int> InsertUserAsync(User user, CancellationToken cancellationToken = default);

    // Reference resolution. Each of these is scoped to the tenant (or, for tenants themselves, to the
    // caller's membership) rather than being a bare Guid lookup, so a reference belonging to another
    // tenant does not resolve and cannot be acted on or probed for. This is why they are explicit
    // methods and not IReferenceMapper registrations - that interface takes only a Guid.
    Task<int> FindTenantIdByRefIdForUserAsync(Guid tenantRef, int userId, CancellationToken cancellationToken = default);
    Task<int> FindRoleIdByRefIdAsync(Guid roleRef, int tenantId, CancellationToken cancellationToken = default);
    Task<int> FindGroupIdByRefIdAsync(Guid groupRef, int tenantId, CancellationToken cancellationToken = default);
    Task<int> FindUserIdByRefIdInTenantAsync(Guid userRef, int tenantId, CancellationToken cancellationToken = default);

    Task InsertRefreshTokenAsync(RefreshToken token, string ipAddress, CancellationToken cancellationToken = default);
    Task<RefreshToken> GetRefreshTokenAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>Returns the number of rows revoked — 0 means the token was already revoked.</summary>
    Task<int> RevokeRefreshTokenAsync(string token, string ipAddress, string? replacedByToken, CancellationToken cancellationToken = default);

    Task<int> RevokeAllRefreshTokensForUserAsync(int userId, string ipAddress, CancellationToken cancellationToken = default);

    // Account recovery and address verification. As with invitations, the raw token never reaches
    // this layer -- the service hashes it, so a provider that logged its parameters could not leak a
    // usable one.
    Task CreatePasswordResetTokenAsync(int userId, byte[] tokenHash, DateTime expiresAt, string? requestedByIp, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes the new password and revokes every refresh token the account has, in one transaction,
    /// and returns the user id. Throws <see cref="Wbskt.Primitives.Exceptions.SecurityException"/>
    /// when the token is unknown, spent or expired -- one answer for all three.
    /// </summary>
    Task<int> ConsumePasswordResetTokenAsync(byte[] tokenHash, string passwordHash, string? revokedByIp, CancellationToken cancellationToken = default);

    Task CreateEmailVerificationTokenAsync(int userId, byte[] tokenHash, DateTime expiresAt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks the account's address verified and returns the user id. Throws
    /// <see cref="Wbskt.Primitives.Exceptions.SecurityException"/> when the token is not valid.
    /// </summary>
    Task<int> ConsumeEmailVerificationTokenAsync(byte[] tokenHash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Suspends a member in one tenant or lifts it (<c>TenantMember_SetSuspended</c>). Throws 50008 if
    /// it would leave the tenant without an administrator, 50016 if they are not a member.
    /// </summary>
    Task SetMemberSuspendedAsync(int tenantId, int userId, bool isSuspended, CancellationToken cancellationToken = default);

    Task<bool> VerifyPermissionAsync(int userId, int tenantId, int? workspaceId, string permissionSlug, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<string>> GetEffectivePermissionsAsync(int userId, int workspaceId, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<Tenant>> GetTenantsForUserAsync(int userId, CancellationToken cancellationToken = default);
    Task<IPagedList<TenantMemberResponse>> GetTenantMembersAsync(int tenantId, string? search, int skip, int take, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a tenant with its own roles, the owner's membership and a tenant-wide Admin
    /// assignment, plus a default workspace. All of it or none — a tenant missing any part of that
    /// has no administrator, and no endpoint can grant one after the fact.
    /// </summary>
    Task<Guid> CreateTenantAsync(string name, string? description, int ownerUserId, string workspaceName, CancellationToken cancellationToken = default);
    Task UpdateTenantAsync(int tenantId, string name, string? description, CancellationToken cancellationToken = default);

    /// <summary>Removes a member and transfers any workspace they owned to <paramref name="newOwnerUserId"/>.</summary>
    Task RemoveTenantMemberAsync(int tenantId, int userId, int newOwnerUserId, CancellationToken cancellationToken = default);

    // Invitations. The token is never stored or accepted in raw form here — the service hashes it,
    // so a provider that logged its parameters could not leak a usable one.
    Task<CreatedInvitation> CreateInvitationAsync(int tenantId, string email, int? roleId, byte[] tokenHash, DateTime expiresAt, int invitedByUserId, CancellationToken cancellationToken = default);
    /// <summary>Throws <see cref="Wbskt.Primitives.Exceptions.SecurityException"/> when no invitation carries that hash.</summary>
    Task<InvitationLookup> GetInvitationByTokenHashAsync(byte[] tokenHash, CancellationToken cancellationToken = default);
    Task<int> AcceptInvitationAsync(byte[] tokenHash, int userId, CancellationToken cancellationToken = default);
    Task RevokeInvitationAsync(Guid invitationRef, int tenantId, CancellationToken cancellationToken = default);
    Task<IPagedList<InvitationResponse>> GetInvitationsAsync(int tenantId, int skip, int take, CancellationToken cancellationToken = default);

    Task<IPagedList<PermissionResponse>> GetPermissionsAsync(int skip, int take, CancellationToken cancellationToken = default);
    Task<IPagedList<RoleResponse>> GetRolesAsync(int tenantId, int skip, int take, CancellationToken cancellationToken = default);
    Task<IPagedList<GroupResponse>> GetGroupsAsync(int tenantId, int skip, int take, CancellationToken cancellationToken = default);

    Task<Guid> InsertRoleAsync(string name, string? description, int tenantId, CancellationToken cancellationToken = default);
    Task UpdateRoleAsync(int roleId, int tenantId, string name, string? description, CancellationToken cancellationToken = default);
    Task DeleteRoleAsync(int roleId, int tenantId, CancellationToken cancellationToken = default);

    Task<Guid> InsertGroupAsync(string name, int? parentGroupId, int tenantId, CancellationToken cancellationToken = default);
    Task UpdateGroupAsync(int groupId, int tenantId, string name, CancellationToken cancellationToken = default);
    Task DeleteGroupAsync(int groupId, int tenantId, CancellationToken cancellationToken = default);

    Task InsertUserGroupAsync(int userId, int groupId, int tenantId, CancellationToken cancellationToken = default);
    Task RemoveUserGroupAsync(int userId, int groupId, int tenantId, CancellationToken cancellationToken = default);

    Task GrantRolePermissionAsync(int roleId, string permissionSlug, bool isDeny, int tenantId, CancellationToken cancellationToken = default);
    Task RemoveRolePermissionAsync(int roleId, string permissionSlug, int tenantId, CancellationToken cancellationToken = default);
    Task GrantUserPermissionAsync(int userId, string permissionSlug, bool isDeny, int tenantId, int? workspaceId, CancellationToken cancellationToken = default);
    Task RemoveUserPermissionAsync(int userId, string permissionSlug, int tenantId, int? workspaceId, CancellationToken cancellationToken = default);

    Task AssignUserRoleAsync(int userId, int roleId, int tenantId, int? workspaceId, CancellationToken cancellationToken = default);
    Task RemoveUserRoleAsync(int userId, int roleId, int tenantId, int? workspaceId, CancellationToken cancellationToken = default);
    Task AssignGroupRoleAsync(int groupId, int roleId, int tenantId, int? workspaceId, CancellationToken cancellationToken = default);
    Task RemoveGroupRoleAsync(int groupId, int roleId, int tenantId, int? workspaceId, CancellationToken cancellationToken = default);

    // Assignment read-back. Without these an administrator can write the permission graph but never
    // inspect it, which makes an unexpected allow or deny impossible to diagnose through the API.
    Task<IReadOnlyCollection<RoleAssignmentResponse>> GetUserRolesAsync(int userId, int tenantId, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<UserPermissionAssignmentResponse>> GetUserPermissionsAsync(int userId, int tenantId, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<GroupMembershipResponse>> GetUserGroupsAsync(int userId, int tenantId, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<RoleAssignmentResponse>> GetGroupRolesAsync(int groupId, int tenantId, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<RolePermissionAssignmentResponse>> GetRolePermissionsAsync(int roleId, CancellationToken cancellationToken = default);
}
