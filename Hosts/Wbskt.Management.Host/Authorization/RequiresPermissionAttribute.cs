namespace Wbskt.Management.Host.Authorization;

/// <summary>How the permissions named on one <see cref="RequiresPermissionAttribute"/> combine.</summary>
public enum PermissionMatch
{
    /// <summary>The caller needs every permission named.</summary>
    All = 0,

    /// <summary>The caller needs at least one of the permissions named.</summary>
    Any = 1
}

/// <summary>
/// Declares what a workspace-scoped action needs: the caller must be a member of the workspace named
/// by the <c>workspaceRef</c> route value, and hold the permissions named here.
/// <see cref="WorkspacePermissionFilter"/> resolves the workspace once, before model binding, refuses
/// the request if the check fails, and hands the action its internal ID through
/// <see cref="FromWorkspaceAttribute"/>.
/// </summary>
/// <remarks>
/// <para>
/// Several permissions on one attribute need all of them unless <see cref="Mode"/> is
/// <see cref="PermissionMatch.Any"/>. Several attributes on one action must all pass, so "A and (B
/// or C)" is <c>[RequiresPermission(A)] [RequiresPermission(B, C, Mode = PermissionMatch.Any)]</c>.
/// </para>
/// <para>
/// On a controller the attributes are the default for its actions; an action that carries any of
/// its own replaces the controller's rather than adding to them.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class RequiresPermissionAttribute : Attribute
{
    public RequiresPermissionAttribute(params string[] permissions)
    {
        if (permissions is null || permissions.Length == 0)
        {
            throw new ArgumentException("Name at least one permission.", nameof(permissions));
        }

        Permissions = permissions;
    }

    /// <summary>The permission slugs, from <c>PermissionNames</c>.</summary>
    public IReadOnlyList<string> Permissions { get; }

    /// <summary>Whether the caller needs all of <see cref="Permissions"/> (the default) or any one.</summary>
    public PermissionMatch Mode { get; set; } = PermissionMatch.All;

    /// <summary>The permissions the caller lacks for this attribute, or none when it is satisfied.</summary>
    public IReadOnlyList<string> Missing(Func<string, bool> hasPermission)
    {
        var missing = Permissions.Where(p => !hasPermission(p)).ToList();
        return Mode == PermissionMatch.Any && missing.Count < Permissions.Count ? [] : missing;
    }
}
