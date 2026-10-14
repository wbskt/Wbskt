using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Models;
using Wbskt.Models;

namespace Wbskt.Auth.Host.Providers;

/// <summary>Tenants: the tenant itself, its members and their suspension, and invitations to join it.</summary>
internal interface ITenantProvider
{
    // Reference resolution. Scoped to the caller's membership (a tenant) or to the tenant (a member)
    // rather than being a bare Guid lookup, so a reference belonging to another tenant does not
    // resolve and cannot be acted on or probed for.
    Task<int> FindTenantIdByRefIdForUserAsync(Guid tenantRef, int userId, CancellationToken cancellationToken = default);
    Task<int> FindUserIdByRefIdInTenantAsync(Guid userRef, int tenantId, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<Tenant>> GetTenantsForUserAsync(int userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a tenant with its own roles, the owner's membership and a tenant-wide Admin
    /// assignment, plus a default workspace. All of it or none — a tenant missing any part of that
    /// has no administrator, and no endpoint can grant one after the fact.
    /// </summary>
    Task<Guid> CreateTenantAsync(string name, string? description, int ownerUserId, string workspaceName, CancellationToken cancellationToken = default);
    Task UpdateTenantAsync(int tenantId, string name, string? description, CancellationToken cancellationToken = default);

    Task<IPagedList<TenantMemberResponse>> GetTenantMembersAsync(int tenantId, string? search, int skip, int take, CancellationToken cancellationToken = default);

    /// <summary>Removes a member and transfers any workspace they owned to <paramref name="newOwnerUserId"/>.</summary>
    Task RemoveTenantMemberAsync(int tenantId, int userId, int newOwnerUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Suspends a member in one tenant or lifts it (<c>TenantMember_SetSuspended</c>). Throws 50008 if
    /// it would leave the tenant without an administrator, 50016 if they are not a member.
    /// </summary>
    Task SetMemberSuspendedAsync(int tenantId, int userId, bool isSuspended, CancellationToken cancellationToken = default);

    // Invitations. The token is never stored or accepted in raw form here — the service hashes it,
    // so a provider that logged its parameters could not leak a usable one.
    /// <summary>Throws 50017 when one of <paramref name="workspaceIds"/> is not in the tenant.</summary>
    Task<CreatedInvitation> CreateInvitationAsync(int tenantId, string email, int? roleId, byte[] tokenHash, DateTime expiresAt, int invitedByUserId, IReadOnlyCollection<int> workspaceIds, CancellationToken cancellationToken = default);
    /// <summary>Throws <see cref="Wbskt.Primitives.Exceptions.SecurityException"/> when no invitation carries that hash.</summary>
    Task<InvitationLookup> GetInvitationByTokenHashAsync(byte[] tokenHash, CancellationToken cancellationToken = default);
    Task<int> AcceptInvitationAsync(byte[] tokenHash, int userId, CancellationToken cancellationToken = default);
    /// <summary>The invitation's address, or null when it was not outstanding (already used, revoked or unknown).</summary>
    Task<string?> RevokeInvitationAsync(Guid invitationRef, int tenantId, CancellationToken cancellationToken = default);
    Task<IPagedList<InvitationResponse>> GetInvitationsAsync(int tenantId, int skip, int take, CancellationToken cancellationToken = default);
}
