using Wbskt.Primitives.Models;

namespace Wbskt.Primitives.Constants;

public static class Permissions
{
    // Read/manage split, matching the other groups below. Without the read slugs a console that only
    // needs to display roles or members has to be granted the permission to rewrite them.
    public static readonly PermissionSlug UsersRead = PermissionSlug.CreateInternal("users.read");
    public static readonly PermissionSlug UsersManage = PermissionSlug.CreateInternal("users.manage");
    public static readonly PermissionSlug RolesRead = PermissionSlug.CreateInternal("roles.read");
    public static readonly PermissionSlug RolesManage = PermissionSlug.CreateInternal("roles.manage");

    public static readonly PermissionSlug PoliciesRead = PermissionSlug.CreateInternal("policies.read");
    public static readonly PermissionSlug PoliciesManage = PermissionSlug.CreateInternal("policies.manage");
    
    public static readonly PermissionSlug ClientsRead = PermissionSlug.CreateInternal("clients.read");
    public static readonly PermissionSlug ClientsUpdate = PermissionSlug.CreateInternal("clients.update");
    public static readonly PermissionSlug ClientsManage = PermissionSlug.CreateInternal("clients.manage");
    public static readonly PermissionSlug ClientsCommand = PermissionSlug.CreateInternal("clients.command");
    public static readonly PermissionSlug ClientsPing = PermissionSlug.CreateInternal("clients.ping");
    
    public static readonly PermissionSlug WorkflowsRead = PermissionSlug.CreateInternal("workflows.read");
    public static readonly PermissionSlug WorkflowsCreate = PermissionSlug.CreateInternal("workflows.create");
    public static readonly PermissionSlug WorkflowsUpdate = PermissionSlug.CreateInternal("workflows.update");
    public static readonly PermissionSlug WorkflowsDelete = PermissionSlug.CreateInternal("workflows.delete");
    
    public static readonly PermissionSlug WorkspaceJoin = PermissionSlug.CreateInternal("workspace.join");
    public static readonly PermissionSlug LogsRead = PermissionSlug.CreateInternal("logs.read");
}