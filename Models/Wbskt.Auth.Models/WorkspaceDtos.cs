using System.ComponentModel.DataAnnotations;

namespace Wbskt.Auth.Models;

/// <summary>A workspace the caller can see. <c>TenantRefId</c> names the account that owns it, as <c>/api/tenants/{tenantRef}</c> takes it.</summary>
public record WorkspaceResponse(Guid RefId, string Name, string? Description, DateTime CreatedAt, Guid TenantRefId);

/// <summary>What the caller may do in one workspace: their effective permission names there, such as <c>users.read</c>.</summary>
public record WorkspacePermissionsResponse(IReadOnlyCollection<string> Permissions);

// Lengths mirror dbo.Workspaces so over-long input fails validation rather than reaching SQL.
public record CreateWorkspaceRequest(
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
