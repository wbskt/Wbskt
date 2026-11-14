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
    /// Auth host: one workspace with its tenant's plan, by the workspace's RefId, in the shape of
    /// <see cref="WorkspaceAccounts"/>. For the management host to learn a workspace's Id when a caller
    /// with no user token names it, so it takes <see cref="ApiKeyHeader"/>.
    /// </summary>
    public const string WorkspaceAccount = Prefix + "/workspaces/{workspaceRef:guid}/account";

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

    /// <summary>
    /// Auth host: <c>GET</c> a tenant's plan and the limits it runs under now, or <c>PUT</c> its limit
    /// overrides alone, leaving its plan and whoever set it in charge, for the admin host.
    /// </summary>
    public const string AdminTenantLimits = AdminPrefix + "/tenants/{tenantRef:guid}/limits";

    /// <summary>Auth host: <c>PUT</c> a staff plan on a tenant until a date, for the admin host.</summary>
    public const string AdminTenantPlan = AdminPrefix + "/tenants/{tenantRef:guid}/plan";

    /// <summary>Auth host: <c>POST</c> ends a tenant's staff plan now and hands the tenant back to billing.</summary>
    public const string AdminTenantPlanEnd = AdminPrefix + "/tenants/{tenantRef:guid}/plan/end";

    /// <summary>Auth host: <c>POST</c> gives a tenant credits that expire, for the admin host.</summary>
    public const string AdminTenantCreditGrants = AdminPrefix + "/tenants/{tenantRef:guid}/credit-grants";

    /// <summary>Auth host: <c>POST</c> takes back what is left of a credit grant staff gave.</summary>
    public const string AdminTenantCreditGrantRevoke = AdminPrefix + "/tenants/{tenantRef:guid}/credit-grants/{grantRef:guid}/revoke";

    /// <summary>Auth host: <c>POST</c> lifts a person's sign-in lockout and clears their failure count.</summary>
    public const string AdminUserLockoutClear = AdminPrefix + "/users/{userRef:guid}/lockout/clear";

    /// <summary>Auth host: <c>POST</c> mails a person a new email verification link.</summary>
    public const string AdminUserVerificationResend = AdminPrefix + "/users/{userRef:guid}/email-verification/resend";

    /// <summary>Auth host: <c>POST</c> mails a person a password reset link.</summary>
    public const string AdminUserPasswordReset = AdminPrefix + "/users/{userRef:guid}/password-reset";

    /// <summary>Auth host: <c>POST</c> ends every session a person has.</summary>
    public const string AdminUserSessionsRevoke = AdminPrefix + "/users/{userRef:guid}/sessions/revoke";

    /// <summary>Auth host: <c>PUT</c> turns a person's sign-in on or off.</summary>
    public const string AdminUserSignIn = AdminPrefix + "/users/{userRef:guid}/sign-in";

    /// <summary>Auth host: <c>PUT</c> suspends a member of a tenant, or lifts it.</summary>
    public const string AdminTenantMemberSuspension = AdminPrefix + "/tenants/{tenantRef:guid}/members/{userRef:guid}/suspension";

    /// <summary>Auth host: <c>POST</c> removes a member from a tenant.</summary>
    public const string AdminTenantMemberRemove = AdminPrefix + "/tenants/{tenantRef:guid}/members/{userRef:guid}/remove";

    /// <summary>Management host: <c>PUT</c> sets or clears a workspace's monthly credit cap, for the admin host.</summary>
    public const string AdminWorkspaceCreditCap = AdminPrefix + "/workspaces/{workspaceRef:guid}/credit-cap";

    /// <summary>
    /// The admin host's own key, <c>Internal:AdminApiKey</c> on the host that serves an
    /// <see cref="AdminPrefix"/> route. It opens those routes and nothing else, so it can be rotated or
    /// revoked without touching <see cref="ApiKeyHeader"/>.
    /// </summary>
    public const string AdminApiKeyHeader = "X-Wbskt-Admin-Key";
}
