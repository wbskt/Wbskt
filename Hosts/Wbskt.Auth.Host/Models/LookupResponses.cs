namespace Wbskt.Auth.Host.Models;

public record PermissionResponse(string Slug, string? Description);
public record RoleResponse(int Id, string Name, string? Description);
public record GroupResponse(int Id, string Name, int? ParentGroupId);
public record TenantResponse(int Id, Guid RefId, string Name);

public record SetUserActiveRequest(bool IsActive);
