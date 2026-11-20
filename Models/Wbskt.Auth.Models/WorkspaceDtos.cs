using System.Text.Json.Serialization;
using System.ComponentModel.DataAnnotations;

namespace Wbskt.Auth.Models;

/// <summary>
/// A workspace the caller can see. <c>TenantRefId</c> names the account that owns it, as <c>/api/tenants/{tenantRef}</c>
/// takes it. <c>Role</c> is the caller's place in it and <c>MemberCount</c> how many people are members; what they
/// may do there is <c>GET {workspaceRef}/permissions</c>, and who the members are <c>GET {workspaceRef}/members</c>.
/// </summary>
public record WorkspaceResponse(Guid RefId, string Name, string? Description, DateTimeOffset CreatedAt, Guid TenantRefId, WorkspaceRole Role, int MemberCount);

/// <summary>
/// The caller's place in a workspace, for a label: <c>Owner</c> owns it, <c>Admin</c> holds the account's
/// Administrator role (account-wide or for this workspace, directly or through a group), and anyone else is a
/// <c>Member</c>. A label only: a custom role can grant a Member as much as an Admin.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<WorkspaceRole>))]
public enum WorkspaceRole
{
    Owner,
    Admin,
    Member
}

/// <summary>What the caller may do in one workspace: their effective permission names there, such as <c>users.read</c>.</summary>
public record WorkspacePermissionsResponse(IReadOnlyCollection<string> Permissions);

// Lengths mirror dbo.Workspaces so over-long input fails validation rather than reaching SQL.
public record CreateWorkspaceRequest(
    [Required]
    [StringLength(100, MinimumLength = 1)]
    string Name,

    [StringLength(500)]
    string? Description,

    // The tenant to create it in, as WorkspaceResponse.TenantRefId names it. Left out, the server picks
    // the one tenant where the caller may create workspaces and refuses when there is more than one.
    Guid? TenantRefId = null);

/// <summary>A workspace's new name and description. A workspace never moves tenant, so there is no tenant here.</summary>
public record UpdateWorkspaceRequest(
    [Required]
    [StringLength(100, MinimumLength = 1)]
    string Name,

    [StringLength(500)]
    string? Description);

public record AddMemberRequest(
    [Required]
    [EmailAddress]
    [StringLength(100)]
    string Email);

/// <summary>Names the tenant member who becomes the workspace's owner.</summary>
public record TransferOwnershipRequest(
    [Required]
    Guid UserRef);
