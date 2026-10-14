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

    /// <summary>
    /// Auth host: the ids of the workspaces deleted in a window (<c>?since=&amp;until=</c>), for the
    /// management host's deleted workspace sweep. Called with no user behind it, so it takes
    /// <see cref="ApiKeyHeader"/> instead of a token.
    /// </summary>
    public const string DeletedWorkspaces = Prefix + "/workspaces/deleted";

    /// <summary>
    /// The shared key an internal route called by a background job (no user token to forward) checks:
    /// the auth host's <c>Internal:ApiKey</c>, sent by the management host from
    /// <c>Services:AuthInternalApiKey</c>.
    /// </summary>
    public const string ApiKeyHeader = "X-Wbskt-Internal-Key";
}
