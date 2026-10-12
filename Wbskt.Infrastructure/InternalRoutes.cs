namespace Wbskt.Infrastructure;

/// <summary>
/// Routes one host calls on another over the backend network. Every public Traefik router excludes
/// <see cref="Prefix"/> (deploy/compose/docker-compose.yml, pinned by <c>PublicRoutingTests</c>), and
/// these actions are left out of the OpenAPI document. They still authenticate the caller's token:
/// being unreachable from outside is a second wall, not the only one.
/// </summary>
public static class InternalRoutes
{
    public const string Prefix = "api/internal";

    /// <summary>Auth host: a workspace's internal id and the caller's permissions in it, for the management host.</summary>
    public const string ResolveWorkspace = Prefix + "/workspaces/resolve";

    /// <summary>Auth host: the caller's internal user id, for a host whose records are keyed by it (see <c>WbsktClaims</c>).</summary>
    public const string CurrentUser = Prefix + "/users/me";
}
