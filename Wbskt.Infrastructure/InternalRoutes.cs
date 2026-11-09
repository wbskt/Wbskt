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
    /// Auth host: each workspace with its tenant's plan, a page at a time (<c>?cursor=&amp;limit=</c>,
    /// narrowed by <c>tenantId</c> or <c>workspaceId</c>), for the management host's copies in the main
    /// database. Called by a background job and by event handlers, so it takes <see cref="ApiKeyHeader"/>.
    /// </summary>
    public const string WorkspaceAccounts = Prefix + "/workspaces/accounts";

    /// <summary>
    /// Auth host: <c>GET</c> or <c>PUT</c> a tenant's plan (<c>{tenantRef}</c>). For operators and the
    /// payment provider's webhook, never a user, so it takes <see cref="ApiKeyHeader"/>.
    /// </summary>
    public const string TenantPlan = Prefix + "/tenants/{tenantRef:guid}/plan";

    /// <summary>
    /// Auth host: every credit pack bought, with its refund and hold, a page at a time
    /// (<c>?cursor=&amp;limit=</c>), for the management host's reconcile of the main database's grants.
    /// Called by a background job, so it takes <see cref="ApiKeyHeader"/>.
    /// </summary>
    public const string CreditPurchases = Prefix + "/billing/credit-purchases";

    /// <summary>
    /// The shared key an internal route called by a background job (no user token to forward) checks:
    /// the auth host's <c>Internal:ApiKey</c>, sent by the management host from
    /// <c>Services:AuthInternalApiKey</c>.
    /// </summary>
    public const string ApiKeyHeader = "X-Wbskt-Internal-Key";
}
