using System.ComponentModel.DataAnnotations;

namespace Wbskt.Auth.Host.Models;

public record WorkspaceResponse(Guid RefId, string Name, string? Description, DateTime CreatedAt);

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
