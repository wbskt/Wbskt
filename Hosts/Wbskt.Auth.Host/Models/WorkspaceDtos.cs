using System.ComponentModel.DataAnnotations;

namespace Wbskt.Auth.Host.Models;

public record WorkspaceResponse(Guid RefId, string Name, string? Description, DateTime CreatedAt);

// Lengths mirror dbo.Workspaces so over-long input fails validation rather than reaching SQL.
public record CreateWorkspaceRequest(
    [property: Required]
    [property: StringLength(100, MinimumLength = 1)]
    string Name,

    [property: StringLength(500)]
    string? Description);

public record AddMemberRequest(
    [property: Required]
    [property: EmailAddress]
    [property: StringLength(100)]
    string Email);
