using Wbskt.Primitives.Models;

namespace Wbskt.Primitives.Constants;

public static class Permissions
{
    // Read/manage split, matching the other groups below. Without the read slugs a console that only
    // needs to display roles or members has to be granted the permission to rewrite them.
    public static readonly PermissionSlug UsersRead = PermissionSlug.CreateInternal(PermissionNames.UsersRead);
    public static readonly PermissionSlug UsersManage = PermissionSlug.CreateInternal(PermissionNames.UsersManage);
    public static readonly PermissionSlug RolesRead = PermissionSlug.CreateInternal(PermissionNames.RolesRead);
    public static readonly PermissionSlug RolesManage = PermissionSlug.CreateInternal(PermissionNames.RolesManage);

    public static readonly PermissionSlug PoliciesRead = PermissionSlug.CreateInternal(PermissionNames.PoliciesRead);
    public static readonly PermissionSlug PoliciesManage = PermissionSlug.CreateInternal(PermissionNames.PoliciesManage);

    public static readonly PermissionSlug ClientsRead = PermissionSlug.CreateInternal(PermissionNames.ClientsRead);
    public static readonly PermissionSlug ClientsUpdate = PermissionSlug.CreateInternal(PermissionNames.ClientsUpdate);

    /// <summary>
    /// Client lifecycle beyond a status change: deleting a client and rotating its secret. Approving
    /// and revoking stay on <see cref="ClientsUpdate"/>.
    /// </summary>
    public static readonly PermissionSlug ClientsManage = PermissionSlug.CreateInternal(PermissionNames.ClientsManage);
    public static readonly PermissionSlug ClientsCommand = PermissionSlug.CreateInternal(PermissionNames.ClientsCommand);
    public static readonly PermissionSlug ClientsPing = PermissionSlug.CreateInternal(PermissionNames.ClientsPing);

    // Send-panel message templates. Their own group rather than borrowing the clients slugs: a
    // template is workspace content, and letting an operator save one should not carry any right
    // over the clients themselves.
    public static readonly PermissionSlug TemplatesRead = PermissionSlug.CreateInternal(PermissionNames.TemplatesRead);
    public static readonly PermissionSlug TemplatesManage = PermissionSlug.CreateInternal(PermissionNames.TemplatesManage);

    // Authoring the definition — never a running instance of it.
    public static readonly PermissionSlug WorkflowsRead = PermissionSlug.CreateInternal(PermissionNames.WorkflowsRead);
    public static readonly PermissionSlug WorkflowsCreate = PermissionSlug.CreateInternal(PermissionNames.WorkflowsCreate);
    public static readonly PermissionSlug WorkflowsDelete = PermissionSlug.CreateInternal(PermissionNames.WorkflowsDelete);

    /// <summary>
    /// Reserved for editing a workflow in place. Nothing gates on it today — publishing is versioned,
    /// so a revision goes through <see cref="WorkflowsCreate"/>. Kept in the catalogue rather than
    /// removed so existing role configurations that grant it stay valid.
    /// </summary>
    public static readonly PermissionSlug WorkflowsUpdate = PermissionSlug.CreateInternal(PermissionNames.WorkflowsUpdate);

    /// <summary>
    /// Operating a published workflow: starting a manual run, signalling or cancelling one, and
    /// writing a shared variable. Split from <see cref="WorkflowsUpdate"/> so an operator who runs
    /// workflows does not also need the right to rewrite their definitions.
    /// </summary>
    public static readonly PermissionSlug WorkflowsExecute = PermissionSlug.CreateInternal(PermissionNames.WorkflowsExecute);

    public static readonly PermissionSlug WorkspaceJoin = PermissionSlug.CreateInternal(PermissionNames.WorkspaceJoin);
    public static readonly PermissionSlug LogsRead = PermissionSlug.CreateInternal(PermissionNames.LogsRead);
}