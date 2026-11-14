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
    /// Management host: each Business tenant's overage in a month (<c>?year=&amp;month=</c>), for the
    /// invoice. Called by an operator from inside the backend network, so it takes <see cref="ApiKeyHeader"/>.
    /// </summary>
    public const string BusinessOverage = Prefix + "/billing/business-overage";

    /// <summary>
    /// The shared key an internal route called with no user token to forward checks: <c>Internal:ApiKey</c>
    /// on the host that serves it, sent by the management host to the auth host from
    /// <c>Services:AuthInternalApiKey</c>, and by an operator.
    /// </summary>
    public const string ApiKeyHeader = "X-Wbskt-Internal-Key";

    /// <summary>
    /// What the platform admin host (wbskt/Wbskt.Admin) calls to change something on behalf of a staff
    /// member. Only <see cref="AdminApiKeyHeader"/> opens it, never <see cref="ApiKeyHeader"/>.
    /// </summary>
    public const string AdminPrefix = Prefix + "/admin";

    /// <summary>Auth host: a tenant's plan and the limits it runs under now, for the admin host.</summary>
    public const string AdminTenantLimits = AdminPrefix + "/tenants/{tenantRef:guid}/limits";

    /// <summary>
    /// The admin host's own key, <c>Internal:AdminApiKey</c> on the host that serves an
    /// <see cref="AdminPrefix"/> route. It opens those routes and nothing else, so it can be rotated or
    /// revoked without touching <see cref="ApiKeyHeader"/>.
    /// </summary>
    public const string AdminApiKeyHeader = "X-Wbskt-Admin-Key";
}
