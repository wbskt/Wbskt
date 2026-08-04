using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Wbskt.Auth.Host.Models;

// Responses expose RefId only. The internal integer Id is a database concern and must not cross
// the API boundary (see "The ID Boundary" in Docs/Coding.Conventions.md).
public record PermissionResponse(string Slug, string? Description);

public record RoleResponse(Guid RefId, string Name, string? Description);

public record GroupResponse(Guid RefId, string Name, Guid? ParentGroupRefId);

public record TenantResponse(Guid RefId, string Name);

public record CreateRoleRequest(
    [Required]
    [StringLength(100, MinimumLength = 1)]
    string Name,

    [StringLength(255)]
    string? Description);

public record UpdateRoleRequest(
    [Required]
    [StringLength(100, MinimumLength = 1)]
    string Name,

    [StringLength(255)]
    string? Description);

public record CreateGroupRequest(
    [Required]
    [StringLength(100, MinimumLength = 1)]
    string Name,

    Guid? ParentGroupRef);

public record UpdateGroupRequest(
    [Required]
    [StringLength(100, MinimumLength = 1)]
    string Name);

/// <summary>
/// Scope for an assignment. A null <c>WorkspaceRef</c> means tenant-wide, which applies in every
/// workspace of the tenant.
/// </summary>
public record AssignmentScopeRequest(Guid? WorkspaceRef);

/// <summary>
/// Grants or denies a permission directly to a user, at a scope. A null <c>WorkspaceRef</c> means
/// tenant-wide.
/// </summary>
public record GrantPermissionRequest(
    [Required]
    [StringLength(100)]
    string Slug,

    bool IsDeny = false,

    Guid? WorkspaceRef = null);

/// <summary>
/// Grants or denies a permission on a role. Deliberately has no workspace scope: a role permission
/// is part of the role definition, and the scope is chosen when the role is assigned. Sharing
/// <see cref="GrantPermissionRequest"/> here would accept a <c>WorkspaceRef</c> that could only be
/// ignored, so the caller would get a success response for a scope that was never applied.
/// <para>
/// Unmapped members are rejected rather than dropped. Removing the property alone is not enough —
/// the default is to ignore what it cannot bind, which is the same silent success by another route.
/// This turns a caller who still sends <c>workspaceRef</c> into a 400 that names the mistake.
/// </para>
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public record GrantRolePermissionRequest(
    [Required]
    [StringLength(100)]
    string Slug,

    bool IsDeny = false);

public record SetUserActiveRequest(bool IsActive);

public record CreateTenantRequest(
    [Required]
    [StringLength(100, MinimumLength = 1)]
    string Name,

    [StringLength(500)]
    string? Description);

public record UpdateTenantRequest(
    [Required]
    [StringLength(100, MinimumLength = 1)]
    string Name,

    [StringLength(500)]
    string? Description);

/// <summary>
/// Invites an address to a tenant. A null <c>RoleRef</c> creates a member with no role, which an
/// administrator then assigns — the same end state as inviting and assigning separately.
/// </summary>
public record CreateInvitationRequest(
    [Required]
    [EmailAddress]
    [StringLength(100)]
    string Email,

    Guid? RoleRef = null);

/// <summary>
/// A pending invitation as listed by an administrator. Deliberately carries no token: the raw value
/// is returned once, by the call that issued it, and is not recoverable afterwards.
/// </summary>
public record InvitationResponse(Guid RefId, string Email, Guid? RoleRef, string? RoleName, DateTime ExpiresAt, DateTime CreatedAt);

/// <summary>
/// The result of issuing an invitation. <c>Token</c> is the only time the raw token exists outside
/// the invitee's hands — only its hash is stored.
/// <para>
/// It is returned to the caller rather than emailed because this codebase has no mail transport
/// (TODO(arch): deliver the invitation directly once one exists). Until then the administrator sends
/// the link, which is the "copy invite link" behaviour of most consoles anyway.
/// </para>
/// </summary>
public record CreatedInvitationResponse(Guid RefId, string Email, DateTime ExpiresAt, string Token);

public record AcceptInvitationRequest(
    [Required]
    [StringLength(255)]
    string Token);

/// <summary>Identifies the tenant just joined, so a client can navigate straight into it.</summary>
public record AcceptInvitationResponse(Guid TenantRef, string TenantName);

/// <summary>
/// An invitation as read back before it is redeemed. Not a response type — registration uses it to
/// reject a bad token before creating an account, rather than after.
/// </summary>
public record InvitationLookup(Guid RefId, Guid TenantRef, string TenantName, string Email, DateTime ExpiresAt, bool IsLive);

/// <summary>A user as seen by tenant administration.</summary>
public record TenantMemberResponse(Guid RefId, string Username, string Email, bool IsActive);

/// <summary>A role held by a user or group, with the scope it was granted at.</summary>
public record RoleAssignmentResponse(Guid RoleRef, string RoleName, Guid? WorkspaceRef);

/// <summary>A permission granted directly to a user, with its scope and whether it is a denial.</summary>
public record UserPermissionAssignmentResponse(string Slug, bool IsDeny, Guid? WorkspaceRef);

/// <summary>A permission attached to a role definition. Role permissions are unscoped.</summary>
public record RolePermissionAssignmentResponse(string Slug, bool IsDeny);

public record GroupMembershipResponse(Guid GroupRef, string GroupName);
