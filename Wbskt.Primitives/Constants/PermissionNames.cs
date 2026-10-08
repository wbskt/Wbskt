namespace Wbskt.Primitives.Constants;

/// <summary>
/// The permission slugs as constants, for attribute arguments (<c>[RequiresPermission(...)]</c>),
/// which cannot take a <see cref="Models.PermissionSlug"/>. <see cref="Permissions"/> is built from
/// these, so the two cannot drift.
/// </summary>
public static class PermissionNames
{
    public const string UsersRead = "users.read";
    public const string UsersManage = "users.manage";
    public const string RolesRead = "roles.read";
    public const string RolesManage = "roles.manage";
    public const string PoliciesRead = "policies.read";
    public const string PoliciesManage = "policies.manage";
    public const string ClientsRead = "clients.read";
    public const string ClientsUpdate = "clients.update";
    public const string ClientsManage = "clients.manage";
    public const string ClientsCommand = "clients.command";
    public const string ClientsPing = "clients.ping";
    public const string TemplatesRead = "templates.read";
    public const string TemplatesManage = "templates.manage";
    public const string WorkflowsRead = "workflows.read";
    public const string WorkflowsCreate = "workflows.create";
    public const string WorkflowsDelete = "workflows.delete";
    public const string WorkflowsUpdate = "workflows.update";
    public const string WorkflowsExecute = "workflows.execute";
    public const string WorkspaceJoin = "workspace.join";
    public const string LogsRead = "logs.read";
}
