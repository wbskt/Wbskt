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
    [property: Required]
    [property: StringLength(100, MinimumLength = 1)]
    string Name,

    [property: StringLength(255)]
    string? Description);

public record UpdateRoleRequest(
    [property: Required]
    [property: StringLength(100, MinimumLength = 1)]
    string Name,

    [property: StringLength(255)]
    string? Description);

public record CreateGroupRequest(
    [property: Required]
    [property: StringLength(100, MinimumLength = 1)]
    string Name,

    Guid? ParentGroupRef);

public record UpdateGroupRequest(
    [property: Required]
    [property: StringLength(100, MinimumLength = 1)]
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
    [property: Required]
    [property: StringLength(100)]
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
    [property: Required]
    [property: StringLength(100)]
    string Slug,

    bool IsDeny = false);

public record SetUserActiveRequest(bool IsActive);

/// <summary>A user as seen by tenant administration.</summary>
public record TenantMemberResponse(Guid RefId, string Username, string Email, bool IsActive);

/// <summary>A role held by a user or group, with the scope it was granted at.</summary>
public record RoleAssignmentResponse(Guid RoleRef, string RoleName, Guid? WorkspaceRef);

/// <summary>A permission granted directly to a user, with its scope and whether it is a denial.</summary>
public record UserPermissionAssignmentResponse(string Slug, bool IsDeny, Guid? WorkspaceRef);

/// <summary>A permission attached to a role definition. Role permissions are unscoped.</summary>
public record RolePermissionAssignmentResponse(string Slug, bool IsDeny);

public record GroupMembershipResponse(Guid GroupRef, string GroupName);
