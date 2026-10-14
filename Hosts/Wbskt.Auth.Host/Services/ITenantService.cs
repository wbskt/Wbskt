using Wbskt.Auth.Models;
using Wbskt.Infrastructure;
using Wbskt.Models;

namespace Wbskt.Auth.Host.Services;

/// <summary>
/// Tenants: the tenant itself, its members and their suspension, and invitations to join it. Roles,
/// groups and permissions live in <see cref="IAccessControlService"/>.
/// <para>
/// The tenant arrives resolved and authorized: <c>TenantPermissionFilter</c> maps the route's
/// <c>tenantRef</c> to its id and checks the caller's tenant-wide permission before the action
/// runs, so a method here only resolves the references named inside the tenant. The caller's id is
/// taken where the operation records who did it or refuses the caller acting on themselves.
/// </para>
/// </summary>
public interface ITenantService
{
    Task<Result<IReadOnlyCollection<TenantResponse>>> GetTenantsForUserAsync(int callerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a tenant with the caller as its administrator. The only endpoint here that is not
    /// gated on an existing tenant, because there is nothing yet to hold a permission in.
    /// </summary>
    Task<Result<TenantResponse>> CreateTenantAsync(int callerId, CreateTenantRequest request, CancellationToken cancellationToken = default);
    Task<Result> UpdateTenantAsync(int tenantId, UpdateTenantRequest request, CancellationToken cancellationToken = default);

    Task<Result<IPagedList<TenantMemberResponse>>> GetMembersAsync(int tenantId, string? search, int skip, int take, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a member from a tenant, transferring any workspace they owned to the caller. Distinct
    /// from deactivating the account, which locks the user out of every tenant they belong to.
    /// </summary>
    Task<Result> RemoveTenantMemberAsync(int callerId, int tenantId, Guid userRef, CancellationToken cancellationToken = default);
    Task<Result> SetMemberSuspendedAsync(int callerId, int tenantId, Guid userRef, bool isSuspended, string ipAddress, CancellationToken cancellationToken = default);

    Task<Result<CreatedInvitationResponse>> CreateInvitationAsync(int callerId, int tenantId, CreateInvitationRequest request, CancellationToken cancellationToken = default);
    Task<Result<IPagedList<InvitationResponse>>> GetInvitationsAsync(int tenantId, int skip, int take, CancellationToken cancellationToken = default);
    Task<Result> RevokeInvitationAsync(int tenantId, Guid invitationRef, CancellationToken cancellationToken = default);

    /// <summary>
    /// Redeems an invitation for the calling account. Not tenant-scoped: the caller does not know
    /// which tenant the token belongs to, and learning that is the point of the call.
    /// </summary>
    Task<Result<AcceptInvitationResponse>> AcceptInvitationAsync(int callerId, string token, CancellationToken cancellationToken = default);
}
