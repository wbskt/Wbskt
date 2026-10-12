namespace Wbskt.Infrastructure.Authorization;

/// <summary>How the permissions named on one requirement combine.</summary>
public enum PermissionMatch
{
    /// <summary>The caller needs every permission named.</summary>
    All = 0,

    /// <summary>The caller needs at least one of the permissions named.</summary>
    Any = 1
}

/// <summary>
/// What an action needs in the scope its route names (a workspace, a tenant). A filter derived from
/// <see cref="ScopedPermissionFilter{TAttribute}"/> resolves the scope once, before model binding,
/// refuses the request if a requirement fails, and hands the action the scope's internal id.
/// </summary>
/// <remarks>
/// <para>
/// Several permissions on one attribute need all of them unless <see cref="Mode"/> is
/// <see cref="PermissionMatch.Any"/>. Several attributes on one action must all pass, so "A and (B
/// or C)" is <c>[Requires…(A)] [Requires…(B, C, Mode = PermissionMatch.Any)]</c>.
/// </para>
/// <para>
/// On a controller the attributes are the default for its actions; an action that carries any of
/// its own replaces the controller's rather than adding to them.
/// </para>
/// </remarks>
public abstract class PermissionRequirementAttribute : Attribute
{
    protected PermissionRequirementAttribute(string[] permissions)
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

/// <summary>
/// A workspace-scoped action: the caller must be a member of the workspace named by the
/// <c>workspaceRef</c> route value, and hold these permissions in it. The action receives the
/// workspace's internal id through <see cref="FromWorkspaceAttribute"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class RequiresPermissionAttribute(params string[] permissions) : PermissionRequirementAttribute(permissions);
