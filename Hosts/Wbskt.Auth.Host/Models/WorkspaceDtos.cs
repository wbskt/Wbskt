namespace Wbskt.Auth.Host.Models;

public record WorkspaceResponse(Guid RefId, string Name, string? Description, DateTime CreatedAt);
public record CreateWorkspaceRequest(string Name, string? Description);
public record AddMemberRequest(string Email);
