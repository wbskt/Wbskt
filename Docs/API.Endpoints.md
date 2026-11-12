# API Endpoints

Every HTTP surface in the system, by host. For the permission model behind the slugs — scope
semantics, resolution precedence, where the checks live — see [AccessControl.md](AccessControl.md).

**The OpenAPI snapshots are the source of truth for routes and shapes.** The auth and management
hosts' documents are checked in as
[`Tests/Wbskt.E2E.FeatureTests/openapi`](../Tests/Wbskt.E2E.FeatureTests/openapi/README.md)
(`auth.v1.json`, `management.v1.json`), and CI fails when a running host's document differs from
them, so a route change arrives as a reviewed diff there. This page explains the routes; where it
and a snapshot disagree, the snapshot is right and this page is behind.

## Hosts at a glance

| Host | Routed publicly | Callers | How a request is authenticated |
|---|---|---|---|
| **Auth** (`Wbskt.Auth.Host`) | yes | console, other hosts | JWT bearer; the credential endpoints are anonymous and rate limited |
| **Management** (`Wbskt.Management.Host`) | yes | console, client SDK | JWT bearer, plus three deliberately anonymous edges (below) |
| **WebSocket gateway** (`Wbskt.Gateway.WebSocket.Host`, over `Wbskt.Gateway.Core`) | yes | client SDK | JWT bearer on the WebSocket upgrade, header or `access_token` query |
| **MQTT gateway** (`Wbskt.Gateway.MQTT.Host`, over `Wbskt.Gateway.Core`) | yes, MQTT only (TCP 8883 with TLS, 1883 plain; routed by #640); its HTTP side serves health checks only | MQTT clients | The client token as the CONNECT password ([Client.Protocol.MQTT.md](Client.Protocol.MQTT.md)) |
| **Workflow Engine** (`Wbskt.Workflow.Engine.Host`) | **no** — backend network only | management host | shared API key header on `/api/inbound/*` |

**Where a public URL lands.** `api.${DOMAIN}` is the one public API host: tenant administration
(`/api/v1/tenants`, `/api/v1/invitations`) and the auth host's workspace routes (`/api/v1/workspaces`, one
workspace, its `members` and `owner`) go to the auth host, client sign-in and registration to the
client-auth role, and everything else to the management host. `auth.${DOMAIN}` serves sign-in, refresh
and sessions (`/api/auth`, where the refresh cookie is scoped), and for one release still answers the
administration routes too, so the console can move over. Nothing under `/api/internal` is routed
publicly: those are host-to-host calls over the backend network (`InternalRoutes`). The Traefik
labels in `deploy/compose/docker-compose.yml` decide all of this, and `PublicRoutingTests` checks
them against each host's routes.

**The three publicly routed hosts default-deny.** Management, Auth and the WebSocket gateway each set an
authorization `FallbackPolicy`, so a controller that forgets `[Authorize]` cannot be reached
anonymously and the anonymous surface is an explicit, auditable choice:

- **Management host** — the original; the other two were brought into line with it.
- **Auth host** — every controller was already correctly attributed when the fallback was added, so
  it changed no behaviour. It exists for the next endpoint added to the host that owns tenants,
  roles and permissions. `AuthController`'s credential endpoints carry `[AllowAnonymous]`
  individually, and so do the internal routes called with no user token (`GET /api/internal/workspaces/deleted`,
  `GET /api/internal/workspaces/accounts`, `GET`/`PUT /api/internal/tenants/{tenantRef}/plan`), which
  `[InternalApiKey]` guards with a shared key instead.
- **WebSocket gateway** — has no controllers at all. `/ws` requires the `ClientOnly` policy (a client
  token: `type=client`), validated by the same bearer handler as the other hosts. Browsers cannot set
  headers on a WebSocket, so on `/ws` the handler also reads `?access_token=` (`AcceptQueryToken`,
  shared with the management host's SignalR hub). `HostArchitectureTests` fails if a controller
  appears here without stating its posture.
- **Engine host** — not publicly routed, and it has no user tokens. `InboundApiKeyMiddleware`
  requires `X-Wbskt-Api-Key` on every endpoint except the `[AllowAnonymous]` health probes, so a
  controller without `[InboundEndpoint]` is still closed (that attribute only keeps a standby from
  serving it). `HostArchitectureTests` fails if an engine controller lacks it.

Two conventions run through every table below:

- **References, never IDs.** Public routes address resources by opaque `Guid` (`RefId`). Internal
  integer IDs never appear in a URL or a response body.
- **A workspace the caller cannot use is a 403; anything inside it that is not theirs is a 404.**
  An unknown workspace and one the caller is not a member of both answer 403. Within a workspace
  the caller belongs to, an unknown resource reference and another workspace's both answer 404
  `<RESOURCE>_NOT_FOUND`, so the two cannot be told apart. 403 inside a workspace only ever means
  a missing permission. See "The ID Boundary" in [Coding.Conventions.md](Coding.Conventions.md).
  (Endpoints still being moved to this rule are tracked in #233.)

---

## Versioning

Every route on `api.${DOMAIN}` is under **`/api/v1`** (`PublicApi.Prefix`). A breaking change to a
public contract ships as `/api/v2` next to it, never as a change to `/v1`. These stay unversioned
(`PublicApi.Unversioned`):

- `/api/internal/*`: host-to-host only, never routed publicly.
- `/api/auth/*`: the console's sign-in on `auth.${DOMAIN}`; the refresh cookie is scoped to that path.
- `/api/callbacks/*` (management) and `/api/inbound/*` (engine): URLs users paste into third
  parties (webhooks, wake links), which have to keep working as they are.
- `/hubs/notifications`, the WebSocket gateway's `/ws`, health probes and JWKS.

**Aliases for one release (#312).** The auth and management hosts still answer the unversioned
paths (`/api/workspaces/…` and so on) by handling them as `/api/v1/…`
(`UnversionedApiAliasMiddleware`), and they answer those requests with `Deprecation: true`, so a
console or client that hasn't moved yet keeps working. The OpenAPI documents list only `/api/v1`.
Remove the aliases once the console and SDKs no longer call them.

## Errors

Every host answers a failure the same way: RFC 9457 Problem Details, `application/problem+json`,
with two extensions:

```json
{ "status": 404, "title": "Not Found", "detail": "No such client.", "code": "CLIENT_NOT_FOUND", "traceId": "00-…" }
```

- `code` is the application's error code, the thing to branch on. A failure nothing more specific
  named carries one for its status: `AUTH_UNAUTHORIZED` (401, including the bearer challenge),
  `AUTH_FORBIDDEN`, `NOT_FOUND`, `RATE_LIMITED` (429, with `Retry-After`), `UNAVAILABLE` (503, with
  `Retry-After`), `INTERNAL_ERROR`.
- `traceId` ties the response to the host's logs. A 500's `detail` is generic; the cause is only in
  the log under that id.
- Model validation is `VALIDATION_FAILED`, with `errors` mapping each field to its messages.
- A model-validation failure's `detail` is the first field's message.

## Tokens

Every token follows one claims contract (`WbsktClaims`): `sub` is the principal's RefId and `typ`
says what it is, `user` or `client`.

- **User access token** (auth host, audience `wbskt-api`): `sub`, `typ: "user"`, `name`, `email`,
  `sid` (the session). No internal id: a host that keys records by the user's internal id resolves it
  from `sub` (the auth host from its database, the management host from
  `GET /api/internal/users/me`), cached.
- **Client token** (management host, audience `wbskt-socket`): `sub`, `typ: "client"`, `name`,
  `policy_ref`, and for now the client's internal `id` and `workspace_id`, which the WebSocket gateway
  stamps on every event it publishes and has no database to read from.
- Tokens issued before this contract (`sub` the internal id, `uref`, `type`) are still accepted until
  they expire. Revocation ("sign out everywhere", a password change) is keyed by the user's RefId.

## 1. Auth host

### 1.1 Credentials — `api/auth`

Anonymous except where noted, and rate limited per client IP so the password hasher cannot be used as
a work amplifier. There are four buckets, so routine traffic cannot spend the sign-in budget:
`RateLimiting:Authentication` (10/min: register, login, change-password, account/delete,
account/change-email, forgot-password, reset-password), `RateLimiting:TokenRefresh` (120/min: refresh-token, logout),
`RateLimiting:SignUpChallenge` (30/min: register/challenge) and `RateLimiting:EmailVerification`
(10/min: verify-email, resend-verification, account/confirm-email-change,
account/revert-email-change).

The endpoints that hash a password (register, login, change-password, account/delete,
account/change-email, reset-password) also share one
host-wide concurrency limit, `RateLimiting:PasswordHashing` (`PermitLimit`, default twice the CPU
count, with up to `QueueLimit` 20 waiting), so a burst from many addresses queues instead of starving
every other request of CPU. A request that finds the queue full gets 429. A password stored with
older hashing settings is re-hashed with the current ones on the next successful sign-in.

**Refresh cookie.** A browser client sends `X-Refresh-Token-Transport: cookie` on login,
refresh-token, logout, logout-all, change-password and account/confirm-email-change. The refresh token then travels only in an
`HttpOnly; Secure` cookie named `wbskt_refresh`, scoped to `/api/auth`, and is left out of the JSON
body, so script on the page can never read it. refresh-token and logout take an empty body and read
the cookie; refresh-token sets the replacement cookie, and a refused refresh, logout, logout-all,
account/delete and account/revert-email-change clear it, as does a confirm-email-change answered 204. The cookie is only read from a request with that header (which forces a CORS preflight)
and an allowed `Origin`, so another site cannot spend it. `Auth:RefreshCookie:SameSite` defaults to
`Strict`. Without the header everything stays in the body as before, for the SDK, clients and tests;
a body-only refresh-token or logout with no token is still a 400. CORS on this host allows
credentials.

**Sign-up proof of work.** register needs a solved puzzle, so a script that signs up in a loop
pays for each account in CPU time. A proof of work was chosen over a CAPTCHA because it involves no
third party and tracks nobody. `GET register/challenge` returns `challenge`, `difficulty` and
`expiresAt`; the caller finds a `solution` (the console counts up from 0) for which SHA-256 of the
UTF-8 string `challenge + ":" + solution` starts with `difficulty` zero bits, and sends both with the
registration. The challenge is HMAC-signed and stores nothing until it is redeemed; each one registers
at most one account (recorded in Redis when it is connected) and expires after ten minutes. Without
one register answers 400 `SIGNUP_CHALLENGE_REQUIRED`; a forged, expired, unsolved or spent one is 400
`SIGNUP_CHALLENGE_INVALID`, and the client fetches a new one. `Auth:SignUpChallenge` sets `Enabled`
(default true), `Difficulty` (default 18 bits, a second or two in a browser), `LifetimeSeconds` (600)
and `Key` (a base64 HMAC key of at least 32 bytes, checked at startup; unset, each process makes its own, which is fine for one replica but a second auth replica needs a shared one).
`SignUpProofOfWork` in `Wbskt.Auth.Models` is the reference solver, and `WbsktAuthClient.RegisterAsync`
solves it for callers that send no challenge.

**Breached passwords.** register, change-password and reset-password refuse a password that has
appeared in a public data breach with 400 `PASSWORD_BREACHED`, before anything is written (a reset
link stays usable). The check uses Have I Been Pwned's Pwned Passwords range API by k-anonymity:
only the first five hex characters of the password's SHA-1 are sent, with `Add-Padding: true`, and
the match is made here. It fails open: when HIBP errors or takes longer than the timeout, the password
is accepted and a warning logged (`wbskt_auth_breached_password_checks_total{outcome="unavailable"}`).
Sign-in is not checked. `Auth:BreachedPasswordCheck` sets `Enabled` (default true; off in
Development, so the E2E suite never calls HIBP), `MinimumBreachCount` (1), `TimeoutMilliseconds`
(2000) and `BaseUrl`. The host needs outbound HTTPS to `api.pwnedpasswords.com`.

register, forgot-password and resend-verification also respect a per-address mail cooldown
(`Auth:MailCooldown`: one mail of each kind per address per 2 minutes, at most 5 per address an hour),
kept in Redis when it is configured. A suppressed mail is answered with the same 204, and no new token
is issued, so the link already in the inbox keeps working.

| Endpoint | Auth | What it does |
|---|---|---|
| `POST register` | anonymous | Creates an account **and its own tenant** — the tenant row, its `Admin`/`User` roles, the creator's membership, a tenant-wide Admin assignment and a default workspace. Accepts an optional `invitationToken` to join an existing tenant at the same time. Mails a confirmation link; the account cannot sign in until it is followed. Answers **204 whether or not the address is already registered** — a taken address creates nothing and mails its real owner instead. An address that another account moved off within the last 7 days, while its undo link is live, is held for that account the same way: 204, nothing created, nothing mailed. A taken *username* is still a 409 `AUTH_USERNAME_CONFLICT`, and it is decided before the address, whatever the address is, so a username someone holds cannot be used to probe which addresses are registered. Registering again with a username and address you already hold is therefore a 409 and mails nothing. Needs a solved `challenge` and `solution` (above). |
| `GET register/challenge` | anonymous | Issues a sign-up proof-of-work puzzle (above). Stateless and cheap; its own rate-limit bucket. |
| `POST login` | anonymous | Exchanges credentials for an access/refresh token pair (see the refresh cookie below). Every failure mode answers identically, so the endpoint cannot be used to probe which addresses are registered — except `AUTH_EMAIL_UNVERIFIED`, which is returned only *after* the password verifies and therefore tells a caller nothing they did not already prove. |
| `POST refresh-token` | anonymous | Rotating refresh: issues a new pair and revokes the token presented. Presenting a token that was already *rotated* (it has a successor) is treated as a leak — every refresh token for that user is revoked and a `SecurityAlertEvent` is published. A token that was revoked deliberately (logout, a revoked session, a password change) has no successor: it is 401 `AUTH_TOKEN_INACTIVE` and the account's other sessions are left alone. |
| `POST logout` | anonymous | Revokes the one refresh token presented, and the access token issued with it. Succeeds whether or not it existed. |
| `POST logout-all` | authenticated | Revokes the caller's entire refresh-token set. |
| `POST change-password` | authenticated | Takes `currentPassword` and `newPassword`. Writes the new password and revokes **every** session the account has in one transaction, then returns a fresh token pair for the caller. A wrong current password is 400 `AUTH_CURRENT_PASSWORD_INVALID` (not 401, so a client does not sign out) and counts towards the lockout below; a locked account is 403 `AUTH_ACCOUNT_LOCKED`. Rate limited like the anonymous endpoints. |
| `POST account/delete` | authenticated | Deletes the caller's account. Takes `password`, checked like `change-password`'s current password (400 `AUTH_CURRENT_PASSWORD_INVALID`, counts towards the lockout, 403 `AUTH_ACCOUNT_LOCKED`). Tenants the caller is the only member of are deleted with their workspaces (each publishes `WorkspaceDeletedEvent`); from every other tenant the caller is removed, their workspaces passing to another administrator. 409 `ACCOUNT_SOLE_ADMINISTRATOR` while nobody else administers a tenant others belong to (the message names it), so promote someone first. Leaving each shared tenant is announced as a removal (`MemberRemovedEvent`), and the deletion as `AccountDeletedEvent`. A deactivated account cannot delete itself. The `Users` row is kept but anonymised (username, address and password cleared), so the address can register again and audit references still resolve. Every session and access token ends. **204** on success. |
| `PATCH account` | authenticated | Changes fields of the caller's account: for now `username`, a display name (sign-in is by email). 3 to 50 characters, unique (409 `USERNAME_TAKEN`), and not a reserved name or a look-alike of one such as `admin`, `Adm1n_`, `the.support.team` or anything containing `wbskt` (400 `USERNAME_RESERVED`; sign-up refuses the same names). At most one change every 30 days (400 `USERNAME_CHANGED_RECENTLY`, whose message says when). A request naming no field is 400 `ACCOUNT_NO_CHANGES`; the current name again changes nothing. Returns a fresh access token for this session carrying the new name, with no refresh token, since the session's own keeps working. Raises `UserProfileChangedEvent`. |
| `POST account/change-email` | authenticated | Takes `newEmail` and `password` (checked like `change-password`'s, counted towards the lockout). Mails a confirm link to the new address, valid 24 hours; nothing changes until it is followed, and a new request replaces one still pending. An address another account uses is 409 `EMAIL_CHANGE_CONFLICT` and says no more; the current address is 400 `EMAIL_UNCHANGED`. The per-address mail cooldown applies, and a request it holds back answers **204** like any other. |
| `POST account/confirm-email-change` | anonymous | Takes the `token` from the confirm link. Switches the account to the new address (checked free again, and not held by another account's live undo; 409 `EMAIL_CHANGE_CONFLICT`), ends **every** session and access token, and mails the old address a link that undoes the change for 7 days. A caller signed in as the same account gets a fresh token pair (**200**); anyone else gets **204** and signs in with the new address. A spent, expired or unknown token, or one whose account has moved address since, is 400 `EMAIL_CHANGE_TOKEN_INVALID`. Raises `UserProfileChangedEvent`. Pending invitations sent to the old address still work, since accepting goes by token. |
| `POST account/revert-email-change` | anonymous | Takes the `token` from the undo link mailed to the old address. Puts the old address back whatever it was changed to since, ends every session again, drops any pending change, and spends this undo and any from later changes. An undo from an earlier change stays live, so a second change cannot take away the owner's way back. 400 `EMAIL_CHANGE_TOKEN_INVALID` and 409 `EMAIL_CHANGE_CONFLICT` as above. **204**. Raises `UserProfileChangedEvent` with `how: reverted`. |
| `GET sessions` | authenticated | The caller's live sessions, newest sign-in first: `id` (a GUID that stays the same across refreshes), `createdAt` (the sign-in), `lastUsedAt`, `expiresAt`, `ipAddress` (of the last refresh) and `isCurrent` (the session the request came from). One per signed-in client or browser. |
| `DELETE sessions/{sessionId}` | authenticated | Ends one of the caller's sessions: its refresh token stops working, and so does the access token that client holds (its `sid` claim names the session). 404 `AUTH_SESSION_NOT_FOUND` for an id that is not a live session of theirs. |
| `POST forgot-password` | anonymous | Mails a reset link if the address has a usable account. **Always 204** — same status, same empty body, for a registered address, an unknown one, a deactivated one, and a malformed one. |
| `POST reset-password` | anonymous | Redeems a reset token, writes the new password, and revokes **every** refresh token the account holds — in one transaction, so a session an attacker already has cannot outlive the recovery. Unknown, spent and expired tokens are all `RESET_TOKEN_INVALID`. |
| `POST verify-email` | anonymous | Redeems a confirmation token and marks the address verified. Single-use. |
| `POST resend-verification` | anonymous | Mails a fresh confirmation link. Anonymous by necessity, not oversight: sign-in requires a confirmed address, so an account that needs this cannot hold a token with which to ask. Always 204, like `forgot-password`. |

Access tokens last 15 minutes (`Jwt:AccessTokenLifetime`). A session lasts 7 days without use
(`Jwt:RefreshTokenLifetime`), and 30 days from sign-in however often it is used (`Jwt:SessionLifetime`),
after which refresh answers 401 `AUTH_TOKEN_INACTIVE` and the user signs in again. Logout-all,
deactivation, deleting the account, a password reset or change, and a replayed refresh token each revoke the user's refresh tokens
and, through a per-user watermark shared in Redis, the access tokens already issued, on the auth and
management hosts alike. Logout and ending one session do the same for that session's access token,
through its `sid` claim. Revoking access tokens is best effort: with Redis unreachable they live out
their 15 minutes.

Each account is locked for 15 minutes after 10 wrong passwords, counted across sign-in and
`change-password` and wherever they come from — the per-IP limiter cannot see a guesser spread over
many addresses. While locked, sign-in answers exactly as for a wrong password, so a lock does not
confirm the address has an account. A successful sign-in resets the count; a password reset or
change lifts a lock. Sessions issued before the lock keep working.

Reset and confirmation tokens are stored only as a SHA-256 hash, are single-use, and supersede any
predecessor for the same account. A reset link lasts 1 hour; a confirmation link lasts 24 hours —
longer because it is not a credential for an existing account, and sign-up commonly happens shortly
before someone stops reading their inbox for the day.

Sign-in requires a confirmed address. `Auth:Email:RequireVerifiedEmailForSignIn` can turn that check
off and is false in `appsettings.Development.json` so the end-to-end suite can run without an inbox;
it must stay true anywhere real, and the host logs an error at startup while it is not.

### 1.2 Invitations — `api/v1/invitations`

| Endpoint | Auth | What it does |
|---|---|---|
| `POST accept` | authenticated | Redeems an invitation token into a tenant membership. The account's email must match the address invited, so a leaked link is not by itself enough to join. Every rejection — unknown, expired, revoked, spent, wrong address — is reported identically. 409 `PLAN_MEMBER_LIMIT_REACHED` when the inviting tenant's plan has no room for another member; the invitation stays unspent. Registering with an `invitationToken` answers the same, and the new account and its own tenant are kept. |

Issuing invitations lives under the tenant it belongs to (§1.3). Only the SHA-256 hash of a token is
stored; the raw value is returned once, by the call that created it.

### 1.3 Tenant administration — `api/v1/tenants`

All authenticated. Permissions here must be held **tenant-wide** (`WorkspaceId IS NULL`) — a
workspace-scoped grant deliberately does not satisfy them. References resolve tenant-scoped, so a
role or user belonging to another tenant reads as nonexistent.

**Tenants**

| Endpoint | Permission | What it does |
|---|---|---|
| `GET /` | none | Lists the caller's tenants. The entry point for everything below. |
| `POST /` | none | Creates a tenant the caller administers. Self-serve, hence ungated. |
| `PUT {tenantRef}` | `users.manage` | Renames a tenant or changes its description. |
| `GET {tenantRef}/usage` | `billing.read` | The tenant's plan, its limits (with any negotiated overrides applied) and its seats: `{ plan, limits, workspaces: { used, limit }, members: { used, limit } }`. A member counts suspended or not; a pending invitation does not. Credits, messages, clients and runs are pooled on the management host and read from a workspace's `…/usage`. |

**Billing** (Docs/Billing.md, Payments)

| Endpoint | Permission | What it does |
|---|---|---|
| `GET {tenantRef}/billing` | `billing.read` | `{ plan, subscription, plans, creditPacks }`. `subscription` is null until the tenant subscribes, else `{ plan, interval, status, periodEnd, cancelAtPeriodEnd, pastDueSince, scheduledPlan, scheduledInterval, scheduledAt }`, the last three a downgrade waiting for the period's end (null for none), `status` one of `Pending`, `Active`, `PastDue`, `OnHold`, `Paused`, `Cancelled`, `Failed`, `Expired`. `plans` is the `{ plan, interval }` pairs on offer (`Monthly`, `Yearly`), `creditPacks` the `{ name, credits }` packs; both empty while payments are off. |
| `POST {tenantRef}/billing/checkout` | `billing.manage` | Body `{ plan, interval }` or `{ creditPack }`; answers `{ checkoutUrl }`, the provider's hosted checkout, paid by the caller's email. The plan or credits change when the provider's webhook confirms payment. 400 `BILLING_CHECKOUT_INVALID` (neither or both, or a plan without an interval), 400 `BILLING_OFFER_INVALID` (not on offer), 409 `BILLING_SUBSCRIPTION_ACTIVE` (already subscribed: change plan below, or cancel in the portal), 503 `BILLING_UNAVAILABLE` (payments off) or `BILLING_PROVIDER_UNAVAILABLE` (the provider could not be reached). |
| `POST {tenantRef}/billing/plan` | `billing.manage` | Body `{ plan, interval }`; answers the billing read. An upgrade (higher plan, or monthly to yearly) takes effect at once and the prorated difference is charged now; a downgrade takes effect at the end of the period and shows as `subscription.scheduledPlan` until then. Asking for the current plan drops a scheduled downgrade. 400 `BILLING_OFFER_INVALID`, 409 `BILLING_SUBSCRIPTION_REQUIRED` (no subscription: check out) or `BILLING_SUBSCRIPTION_NOT_CHANGEABLE` (cancelled or behind on payment: use the portal), 503 `BILLING_UNAVAILABLE` or `BILLING_PROVIDER_UNAVAILABLE`. |
| `POST {tenantRef}/billing/portal` | `billing.manage` | `{ url }`, the provider's customer portal: cards, invoices, changing or cancelling. 404 `BILLING_CUSTOMER_NOT_FOUND` before the tenant has paid for anything. |

The provider's webhooks arrive at `POST /api/callbacks/billing/{provider}` (anonymous, unversioned,
not in the API docs): 204 once stored and acted on (or set aside), 401 `BILLING_WEBHOOK_SIGNATURE_INVALID`,
404 `BILLING_PROVIDER_NOT_FOUND`, and a 500 when acting on it failed, so the provider delivers it again.

**Invitations**

| Endpoint | Permission | What it does |
|---|---|---|
| `GET {tenantRef}/invitations` | `users.read` | Lists outstanding invitations. |
| `POST {tenantRef}/invitations` | `users.manage`, and `roles.manage` with a `roleRef` | Issues one against an email address and mails the invitee a link. Optional `roleRef` (granted tenant-wide, so it also needs `roles.manage`; 403 `PERMISSION_UNAUTHORIZED` without it) and `workspaceRefs` (up to 50 workspaces of this tenant the invitee joins on acceptance; one outside it is 403 `WORKSPACE_NOT_FOUND`). Still returns the raw token **once**, so an administrator can deliver it by hand when mail is not an option. The listing returns each invitation's `workspaceRefs`. 409 `PLAN_MEMBER_LIMIT_REACHED` when the tenant already has as many members as its plan allows. |
| `DELETE {tenantRef}/invitations/{invitationRef}` | `users.manage` | Revokes an unredeemed invitation. |

**Roles and their permissions**

| Endpoint | Permission | What it does |
|---|---|---|
| `GET {tenantRef}/roles` | `roles.read` | Lists the tenant's roles. Roles are tenant-owned; a new tenant starts with `Admin` (everything) and `User` (the five `*.read` permissions for a workspace's resources). |
| `POST {tenantRef}/roles` | `roles.manage` | Creates a role. |
| `PUT {tenantRef}/roles/{roleRef}` | `roles.manage` | Renames a role or changes its description. |
| `DELETE {tenantRef}/roles/{roleRef}` | `roles.manage` | Deletes a role and its assignments. |
| `GET {tenantRef}/roles/{roleRef}/permissions` | `roles.read` | Lists the role's allow/deny entries. |
| `POST {tenantRef}/roles/{roleRef}/permissions` | `roles.manage` | Grants or denies a slug on the role. `RolePermissions` is part of the role *definition* and is unscoped — scope is applied when the role is assigned. |
| `DELETE {tenantRef}/roles/{roleRef}/permissions/{permissionSlug}` | `roles.manage` | Removes the entry entirely. Distinct from denying it. |

**Groups**

| Endpoint | Permission | What it does |
|---|---|---|
| `GET {tenantRef}/groups` | `users.read` | Lists groups. Groups nest via `ParentGroupId` and a user inherits from ancestors. |
| `POST {tenantRef}/groups` | `users.manage` | Creates a group, optionally under a parent. |
| `PUT {tenantRef}/groups/{groupRef}` | `users.manage` | Renames a group or re-parents it. |
| `DELETE {tenantRef}/groups/{groupRef}` | `users.manage` | Deletes a group. |
| `GET {tenantRef}/groups/{groupRef}/roles` | `roles.read` | Lists role assignments on the group, with their scope. |
| `POST {tenantRef}/groups/{groupRef}/roles/{roleRef}` | `roles.manage` | Assigns a role to the group. Body carries `workspaceRef` — null means tenant-wide. |
| `DELETE {tenantRef}/groups/{groupRef}/roles/{roleRef}` | `roles.manage` | Removes it. Scope travels as a `workspaceRef` query parameter. |

**Members**

| Endpoint | Permission | What it does |
|---|---|---|
| `GET {tenantRef}/members` | `users.read` | Lists tenant members, optionally filtered by `search`. |
| `GET {tenantRef}/members/{userRef}/roles` | `roles.read` | The member's role assignments and their scopes. |
| `GET {tenantRef}/members/{userRef}/permissions` | `roles.read` | Direct user-level permission overrides. |
| `GET {tenantRef}/members/{userRef}/groups` | `users.read` | Group memberships. |
| `POST` / `DELETE {tenantRef}/members/{userRef}/groups/{groupRef}` | `users.manage`, and `roles.manage` to add to a group that carries roles | Adds or removes a group membership. A group that carries roles, its own or a parent's, grants them, so adding to one also needs `roles.manage` (403 `PERMISSION_UNAUTHORIZED` without it). |
| `POST` / `DELETE {tenantRef}/members/{userRef}/roles/{roleRef}` | `roles.manage` | Assigns or removes a role, scoped tenant-wide or to one workspace. |
| `POST {tenantRef}/members/{userRef}/permissions` | `roles.manage` | Grants or denies a slug directly on the user. A user-level entry outranks anything role-derived. |
| `DELETE {tenantRef}/members/{userRef}/permissions/{permissionSlug}` | `roles.manage` | **Deletes the override, does not deny it.** Denying would leave the override in place; deleting returns the decision to the user's roles. Without this an accidental grant could never be undone through the API. |
| `DELETE {tenantRef}/members/{userRef}` | `users.manage` | Offboards a member: removes every assignment scoped to the tenant and transfers workspaces they owned to the caller. Refuses to remove the last administrator, and refuses self-removal (the transfer would have no recipient). |
| `PATCH {tenantRef}/members/{userRef}` | `users.manage` | Body `{ "isSuspended": bool }`; empty is `MEMBER_UPDATE_EMPTY`. Suspends the member **in this tenant only**: they keep their account, sessions and other tenants, and their assignments here, but hold nothing here until it is lifted. Refuses self-suspension (`AUTH_CANNOT_SUSPEND_SELF`) and leaving the tenant without an administrator. |
| `POST {tenantRef}/leave` | member (none) | The caller leaves the tenant, suspended or not, and every assignment they held there goes; workspaces they owned pass to another administrator. The same removal deleting the account makes from each shared tenant. 409 `TENANT_LEAVE_LAST` when it is their only tenant, `TENANT_LEAVE_SOLE_MEMBER` when nobody else belongs to it, `TENANT_SOLE_ADMINISTRATOR` when nobody else administers it (the message names it). Cached workspace access is dropped, and the tenant's log records `MemberLeftEvent`. **204**. |

**Catalogue**

| Endpoint | Permission | What it does |
|---|---|---|
| `GET {tenantRef}/permissions` | `roles.read` | The permission catalogue, for building a role editor. Read-only by design: the catalogue is code-defined in `Wbskt.Primitives/Constants/Permissions.cs`, and a slug invented at runtime could not gate anything. |

The member, invitation, role, group and permission lists page like the management host's (see
[Paging](#2-management-host)): `cursor` and `limit` (default 100, clamped to 1–200), answering
`{ items, nextCursor, totalCount }`. The lists that return a bare array (tenants, a role's permissions, a group's roles, a
member's roles, groups and permissions) are unchanged until the console reads pages.

### 1.4 Workspaces — `api/v1/workspaces`

| Endpoint | Permission | What it does |
|---|---|---|
| `POST /api/internal/workspaces/resolve` | membership only | **Backend network only.** **The gate every other host depends on.** Resolves a workspace reference to its internal ID plus the caller's effective permission slugs there. The management host calls this before every workspace-scoped request. Answers 403 for both "not a member" and "no such workspace" and 401 only when the caller cannot be identified at all — the management host relies on that distinction, and treats any other status as a server fault. |
| `GET /api/internal/workspaces/deleted?since=&until=` | shared internal key | **Backend network only.** The ids of the workspaces deleted in the window, for the management host's deleted workspace sweep. No user is behind that call, so the action is `[AllowAnonymous]` and `[InternalApiKey]` instead: `X-Wbskt-Internal-Key` must match the auth host's `Internal:ApiKey` (`AUTH_INTERNAL_API_KEY`; management sends it from `Services:AuthInternalApiKey`). Without a configured key Production answers 503, as the engine does. |
| `GET /api/internal/workspaces/accounts?cursor=&limit=&tenantId=&workspaceId=` | shared internal key | **Backend network only.** Each workspace with its tenant's plan (`workspaceId`, `tenantId`, `tenantRefId`, `planCode`, `limitOverrides`, `planRevision`), a page at a time by workspace id, for the management host's copies in `dbo.WorkspaceAccounts`: all of them for its periodic sync, one tenant's after `TenantPlanChangedEvent`, one workspace after `WorkspaceCreatedEvent`. Guarded like `deleted` above. |
| `GET /api/internal/tenants/{tenantRef}/plan` | shared internal key | **Backend network only.** The tenant's plan, negotiated limits and plan revision. `TENANT_NOT_FOUND` (404) for an unknown tenant. |
| `PUT /api/internal/tenants/{tenantRef}/plan` | shared internal key | **Backend network only.** For operators and, once billing lands, the payment webhook: puts the tenant on `planCode` with optional `limitOverrides` (replaced whole). An optional `expectedRevision` refuses the change with `TENANT_PLAN_CHANGED` (409) if the plan moved since it was read; a change that loses a race gets the same. Asking for the plan already held changes nothing and sends nothing. A change raises `planRevision` and writes `TenantPlanChangedEvent` to the outbox in the same transaction. An optional `contractClients` (Business only, at least 2,000) sizes a Business contract: its clients, messages and credits are derived from it (`BusinessContract`) unless `limitOverrides` sets them. `PLAN_CODE_INVALID` / `PLAN_LIMIT_INVALID` / `PLAN_CONTRACT_INVALID` (400) for an unknown plan, a negative limit or a contract size on another plan or under the minimum. |
| `GET /` | none | Lists workspaces the caller belongs to. Each carries `tenantRefId`, the account that owns it, for the `api/v1/tenants/{tenantRef}` routes (the member search a workspace's add box uses, for one). |
| `POST /` | `workspaces.create` **tenant-wide** in the chosen tenant | Creates a workspace and makes the caller its owner. `tenantRefId` in the body names the tenant; left out, it is the one tenant where the caller holds `workspaces.create`, and 400 `TENANT_REQUIRED` when there is more than one. A tenant the caller is not in is 403 `TENANT_NOT_FOUND`, one where they lack the permission 403 `PERMISSION_UNAUTHORIZED`. Admin roles hold it, so a tenant's creator can create there; an invited member cannot spend the inviting tenant's workspace limit unless granted it. Someone whose Admin role was taken away in their own tenant can no longer create there. The owner automatically receives the tenant's `Admin` role scoped to it. 409 `PLAN_WORKSPACE_LIMIT_REACHED` when the tenant already has as many workspaces as its plan allows. |
| `PUT {workspaceRef}` | `users.manage` **in that workspace** | Renames a workspace or changes its description (`UpdateWorkspaceRequest`: `name`, `description`). A workspace never moves tenant. |
| `PUT {workspaceRef}/owner` | `users.manage` in that workspace | Body `{ "userRef": guid }`. Makes another member of the workspace's tenant its owner, adding them to the workspace if needed. The previous owner stays a member and can then be removed. Someone outside the tenant is 404 `USER_NOT_FOUND`. |
| `DELETE {workspaceRef}` | `users.manage` in that workspace | Deletes the workspace and every assignment scoped to it. What the management host holds for it (registration policies, clients, workflows, schedules, webhook triggers) is retired shortly after, by an event the delete queues in the same transaction, so it is sent even if the auth host stops or the broker is down at that moment. The delete is also recorded in `dbo.DeletedWorkspaces`, and the management host's daily deleted workspace sweep (`DeletedWorkspaceSweeper`) retires any workspace deleted in the last week that still has live policies, clients, workflows or runs: the backstop for an event lost before the outbox existed, or one whose handler ran out of retries. Each repair is counted in `wbskt_management_workspaces_retired_by_sweep_total` and raises `DeletedWorkspaceRetiredBySweep`. |
| `GET {workspaceRef}/permissions` | membership only | The caller's effective permissions there, `{ permissions: [slug] }`, sorted, so a client shows only what they may do (the members page without Add and Remove for someone with `users.read` alone). A workspace they are not in is 403, as on the other routes here. |
| `GET {workspaceRef}/members` | `users.read` in that workspace | Lists members, one page at a time. `isOwner` marks the workspace's owner, so a client can badge them and hide Remove (which refuses the owner with `WORKSPACE_OWNER_PROTECTED`). The tenant member list leaves the field out. |
| `POST {workspaceRef}/members` | `users.manage` in that workspace | Adds an existing **tenant member** to the workspace. It will not pull in a non-member: doing so used to let anyone with `users.manage` in one workspace capture any account in the system knowing only its email. An unknown address and a non-member are reported identically. |
| `DELETE {workspaceRef}/members/{userRef}` | `users.manage` in that workspace | Removes a member and any assignment scoped to the workspace. The owner cannot be removed. |

### 1.5 Operational

| Endpoint | Auth | What it does |
|---|---|---|
| `GET /healthz` | anonymous | Liveness — the process is up. Probes nothing, so a dependency failure cannot trigger a restart loop. |
| `GET /healthz/ready` | anonymous | Readiness — dependencies reachable. 503 when not. This is what the compose healthcheck consumes, so it also drives Traefik's routing table and `deploy.sh`'s health wait. |
| `GET /openapi`, Scalar reference | anonymous, **development only** unless `ApiDocs:Enabled` (`API_DOCS_ENABLED`) is true | API docs. Explicitly exempted from the default-deny fallback policy. |

---

## 2. Management host

Every workspace-scoped route is `api/v1/workspaces/{workspaceRef:guid}/…` and resolves through
`POST /api/internal/workspaces/resolve` on the auth host before doing anything else. That call is the
membership gate; the permission slug is the second gate.

**Paging.** Every list endpoint takes `cursor` and `limit` and answers with one shape,
`{ items, nextCursor, totalCount? }`. Leave `cursor` out for the first page; pass the previous
page's `nextCursor` back for the next one, until it is null. The cursor is opaque: store it and send
it back, never build or parse it. A cursor no page handed out is 400 `PAGE_CURSOR_INVALID`. `limit`
is clamped to 1–200 rather than rejected, and each list has its own default. Lists that count
cheaply (clients, policies, templates, workflows) also return `totalCount`. Lists that are never long (a client's tags and state, a workflow's
versions) use the same shape and return everything on one page.

The parameters lists took before (`skip`, `take`, `top`, `fromEventId`), the `X-Total-Count`
header and the `runs`/`events` copies of `items` were removed before launch (#351).

**Time ranges.** `from` and `to` are ISO 8601 instants with an offset (`DateTimeOffset`) on every
endpoint that takes them. `to` defaults to now and `from` to a per-endpoint span before it; a range
that is empty or backwards is 400 `TIME_RANGE_INVALID`, and one longer than 400 days is 400
`TIME_RANGE_TOO_LONG`.

> **A reference is not a scope.** Resolving `workspaceRef` establishes which workspace the caller is
> acting in. It says nothing about whether the `clientRef` or `runRef` in the same route belongs
> to it — each endpoint checks that separately.

### 2.1 Clients — `…/clients`

| Endpoint | Permission | What it does |
|---|---|---|
| `GET /` | `clients.read` | Lists clients, filterable by `status`, `name`, `tag` and `policyRefId` (case-insensitive; an invalid one is 400 `CLIENT_TAG_INVALID`). `status` can be repeated to list several statuses together (`?status=2&status=3` is every removed client). Each client carries its `tags`. Paged, with `totalCount`. |
| `GET counts` | `clients.read` | How many clients the workspace has in each status, and how many are connected now (`{ total, pending, registered, revoked, rejected, connected }`), in one read. `policyRefId` counts one policy's clients (404 for one this workspace does not own). |
| `GET tags` | `clients.read` | Every tag in use in the workspace with its client count (`{ tag, clientCount }`), sorted, for a tag filter. |
| `GET {clientRef}` | `clients.read` | Full client detail: presence, uptime anchor, latency, self-reported SDK metadata and command capabilities, and `system`: the platform features the client said it takes (`["ota"]` for firmware updates; empty when none), and `firmwareVersion`: the version of its own code from its last capabilities; null when it never said. `updatingUntil` (here and on the list) is set when the client last disconnected to install a firmware update: while it is in the future the client counts as updating, not offline; its next connect clears it. |
| `GET {clientRef}/state` | `clients.read` | The client's last-known self-reported state variables. |
| `GET {clientRef}/message-types` | `clients.read` | The message types the client has sent, most recently seen first, as one page (`items`, `totalCount`): `type`, `count`, `firstSeenAt`, `lastSeenAt`, `isPlatform` (`state.report` and `capabilities`, which the platform reads itself) and `samplePayload` (the latest payload of that type, as JSON, when it was at most 4,000 characters). Kept up to date as messages arrive (`dbo.ClientMessageTypes`, written in batches), so it is one indexed read. Types compare ignoring case. At most 100 types per client; past that new ones are not recorded. Counts start from when the table was deployed, not from the client's first message, and are approximate: a batch redelivered after it was written is counted twice. |
| `PATCH {clientRef}` | `clients.update` | Changes a client's `name` (1–100 characters), its `status` (approving or revoking it; approving enforces the policy's `MaxClients` ceiling), or both. A field left out stays as it is; naming neither is 400 `CLIENT_UPDATE_EMPTY`. The status changes first, so a refused status leaves the name unchanged too. |
| `PATCH status` | `clients.update` | Gives up to 100 clients one status (`{ clientRefIds, status }`), each handled as the single-client endpoint would. Answers 200 with `updated` and `failed` (ref, code, message), so one client that cannot change (the policy is full, or it is not in this workspace) does not stop the rest. Approvals are taken in the order given. |
| `DELETE {clientRef}` | `clients.manage` | Deletes a client with its capabilities and state, closes its connection and refuses its still-valid token. Its event-log history stays. The client must register again to come back. |
| `POST {clientRef}/rotate-secret` | `clients.manage` | Replaces the client's secret and returns the new one once (`{ clientRefId, secret }`). The old secret stops working, tokens issued before the rotation are refused, and the live connection is closed. |
| `PUT {clientRef}/tags` | `clients.update` | Replaces the client's tags (`{ tags }`; an empty list clears them) and returns them as stored. Tags are trimmed and lower-cased, and repeats collapse. Each is 1–32 letters, digits, spaces, `-`, `_` or `.`, starting and ending with a letter or digit (else 400 `CLIENT_TAG_INVALID`); at most 10 per client (else 400 `CLIENT_TAGS_TOO_MANY`). |
| `POST {clientRef}/commands` | `clients.command` | Sends a command to a client and returns a `commandId` for correlating the delivery/ack events that follow, with `status` `sent` or `queued`. Rejects reserved protocol message types and payloads over 32 KiB. A connected client gets it at once. An offline, registered client has it queued (`status: "queued"`, `expiresAt` = when it stops waiting) and delivered, oldest first, when it next connects; a client that drops before delivery has the command put back in its queue. Queuing is refused with 409 `COMMAND_QUEUE_FULL` past `CommandQueue:MaxPerClient` waiting commands (default 100), and with 409 `CLIENT_INACTIVE` for an offline client that is pending, revoked or rejected. Optional `expiresAt` (at most 24 h ahead, else 400 `COMMAND_EXPIRY_INVALID`); without one a queued command waits `CommandQueue:DefaultExpiryMinutes` (24 h). A command that expires while queued, or whose client is revoked, ends with `ClientCommandFailedEvent`; past `expiresAt` the gateway does not send the command and the SDK refuses it. A workflow's send-message step goes through the same sender (`IClientCommandSender`) and its command always expires when the step would stop waiting for the answer: its ack timeout, 30 s by default. |
| `POST {clientRef}/ota` | `clients.firmware` | Tells the client to update its firmware (`sys.ota.update`): `{ url, sha256, version?, size?, expiresAt? }`. `url` must be `https` (400 `OTA_URL_INVALID`), `sha256` 64 hex digits (400 `OTA_SHA256_INVALID`), `version` at most 50 characters (400 `OTA_VERSION_INVALID`), `size` a byte count (400 `OTA_SIZE_INVALID`). Refused with 409 `OTA_NOT_SUPPORTED` unless the client's last capabilities list `ota` under `system`. Sent like a command, with the same answers and errors: connected now, or queued for an offline client. It always expires: `expiresAt`, or `CommandQueue:DefaultExpiryMinutes` from now. A newer update cancels any older one still queued for the client (each with `ClientCommandCancelledEvent`), since the client takes one at a time and would otherwise install the oldest. The `url` is kept as given, query string included, in the queued-command list and the event log, so anyone with `clients.read` sees it: use a presigned URL that expires with the command, not a long-lived secret. The client reports progress with `sys.ota.status`, raised as `ClientOtaStatusEvent` (`OnClientOtaStatusEvent` to the client's watchers, and in its activity): `accepted`, `downloading` (with `progress` when the client can send it), `installed` with `version` once the new firmware connects, or `rejected` / `failed` with `error`. |
| `GET {clientRef}/commands?status=queued` | `clients.read` | The commands waiting for an offline client, oldest first, as one page (`items`, `totalCount`): `commandId`, `type`, `payload`, `queuedAt`, `expiresAt`. `status` must be `queued` (400 `COMMAND_STATUS_INVALID` otherwise); sent commands are in the comms feed. |
| `DELETE {clientRef}/commands/{commandId}` | `clients.command` | Cancels a waiting command (204, raising `ClientCommandCancelledEvent`); 404 `COMMAND_NOT_QUEUED` once it was delivered, expired or cancelled. |
| `POST {clientRef}/ping` | `clients.ping` | Triggers a round-trip latency measurement. |
| `GET {clientRef}/readings` | `clients.read` | One numeric state variable's history (`name`, required) over `from`/`to` (default the last day, at most 740 days), as `{ start, count, min, avg, max }` per `bucket` (seconds, or `30s`, `5m`, `1h`, `1d`; picked for about 500 buckets when left out). Readings older than the plan's raw days are kept as hourly roll-ups (Docs/Billing.md, Readings), so an older range has an hour's resolution at best; the counts and averages stay exact. |
| `GET {clientRef}/readings/csv` | `clients.read` | Every raw reading over `from`/`to` as CSV, for one `name` or all. Only raw readings: history rolled up into hours is not exported. At most 100,000 rows (else 400 `READINGS_RANGE_TOO_LARGE`). |
| `GET {clientRef}/comms` | `logs.read` | Recent in/out message history, filterable by `direction=in\|out`. Backfills the Live Comms panel before the realtime stream attaches. Uses `logs.read` rather than `clients.read` because it is a projection of the event log — so the client detail page needs both grants to render fully. Paged like the event log. |

The routes the API style guide replaced were removed before launch (#351): `GET policy/{policyRef}`
(use `GET ?policyRefId=`), `PATCH {clientRef}/status` and `PATCH {clientRef}/name` (use
`PATCH {clientRef}`), and `POST {clientRef}/command` (use `/commands`).

Command and ping publish onto the event bus, and the gateway dispatches on `ClientRefId` alone —
it has no workspace of its own to check against. The controller's ownership check is therefore the
only one in the path.

### 2.2 Registration policies — `…/registration-policies`

A policy is the enrolment ticket a client presents: a PIN, an optional client ceiling, and whether
approval is automatic or manual.

| Endpoint | Permission | What it does |
|---|---|---|
| `GET /` | `policies.read` | Lists policies, filterable by `autoApproval` and `name`, each enriched with its registered and connected client counts. |
| `GET {policyRef}` | `policies.read` | One policy, after verifying it belongs to the workspace. |
| `POST /` | `policies.manage` | Creates a policy. The enrolment PIN is generated server-side, not supplied by the caller. |
| `PATCH {policyRef}` | `policies.manage` | Updates name, client limit, auto-approval and enabled state: a full replace, so send every field. Disabling stops the policy accepting new registrations and leaves registered clients as they are; `isEnabled: true` turns it back on. |
| `POST {policyRef}/rotate-pin` | `policies.manage` | Replaces the PIN and returns the policy with the new one. The old PIN stops registering clients; already-registered clients are unaffected. |
| `DELETE {policyRef}` | `policies.manage` | Deletes a policy no client is enrolled through, in any status; otherwise `409 POLICY_HAS_CLIENTS` with the count. Templates pinned to it are unpinned and stay in the workspace; its event-log history is kept. Raises `PolicyDeletedEvent`. |

### 2.3 Message templates — `…/message-templates`

Saved send-panel payloads, optionally pinned to a policy.

| Endpoint | Permission | What it does |
|---|---|---|
| `GET /` | `templates.read` | Lists templates, optionally filtered by `policyRefId`. |
| `POST /` | `templates.manage` | Creates one. Validates that the payload is JSON under 32 KiB and that the message type is not reserved for the platform protocol. |
| `PUT {templateRef}` | `templates.manage` | Replaces a template. |
| `DELETE {templateRef}` | `templates.manage` | Deletes one. |

### 2.4 Event logs — `…/event-logs`

| Endpoint | Permission | What it does |
|---|---|---|
| `GET /` | `logs.read` | The workspace's event log, newest first, `limit` per page (default 50), no total count. Filters, all optional and combined with AND: `eventName` (substring, the older single filter), `eventNames` and `group` (exact names and named groups, repeated or comma-separated; an event named by either is kept), `criticality` (exactly) and `minCriticality` (that level or worse: `Warning` is warnings and errors), `source` (`Console`, `Api`, `Client`, `Workflow`, `System`, repeated or comma-separated; an unknown one is **400** `EVENT_LOG_SOURCE_UNKNOWN`), `sinceId` (only entries newer than this id, for a live view that asks again for what is newer than the newest one it shows), `policyRefId` and `clientRefId` (**404** for one this workspace does not own), `workflowRefId` (every version; a deleted workflow can still be named), `userRefId` (who did it), `from` / `to` (half-open, on the time logged; without them, all of retention), `traffic=include\|exclude` (raw client traffic; included by default) and `q` (text in the entry's data or event name, at most 100 characters). Groups: `people`, `clients`, `policies`, `workflows`, `security` (the warnings and errors about sign-ins, passwords, who has access, keys, PINs, deletions and blocked joins: failed sign-ins, password changes and resets, members removed or suspended, a role's permissions changed, a workspace handed to a new owner); an unknown one is **400** `EVENT_LOG_GROUP_UNKNOWN`. Each entry carries its `id` and `userRefId`: who took the action, for one taken through the API (client and policy changes, commands and pings, workflow publish, deprecate, reinstate, delete, manual run, cancel and signal); null for events with no signed-in user behind them. `source` says where it came from: `Console` (a browser on an allowed console origin), `Api` (a user token used anywhere else), `Client`, `Workflow` (a command a run sent, or a shared variable a run wrote; its `eventData` names the run in `actorWorkflowRefId` and `actorRunRefId`, and the entry is listed under that workflow too; a run's variable writes are client traffic, kept and hidden like it) or `System`; null for sign-ins and for changes people make to their own account. The `people` group holds what happens to people, logged in each workspace it touches: sign-ins (success and failure) and a person's own password changes and resets, signing out everywhere or ending one session, and verifying an email, in every workspace they belong to; invitations sent, revoked, accepted and expired, members removed, suspended and restored, and roles assigned, removed or changed, in every workspace of the tenant; a workspace created, renamed, handed to a new owner, or a member added or removed, in that workspace. An expired invitation is logged at the time it expired. `clientAddress` and `userAgent` are the caller's, for an action taken through the API and for sign-ins and account changes; they are kept on the entry, never inside `eventData`, and go with it at retention. A change event's `eventData` holds `changes: [{field, before, after}]` (policy name, client limit and enabled, including turning it back on; client name and status; workflow version and status; a shared variable's value set by hand, cut to 256 characters; a workspace's name, description and owner; a role's `permissions`, as a comma list where `!slug` is a deny). |
| `GET summary` | `logs.read` | How many entries each group has over `from` / `to` (default the last 7 days, at most 400), and the `total`. Also `events` (event name to count), `people` (the user reference of whoever caused it to count) and `sources` (source to count). With `traffic=exclude`, the total, `events`, `people` and `sources` leave client traffic out. |
| `GET csv` | `logs.read` | The entries `GET /` would list for the same filters, as one CSV file, newest first: `id,createdAt,event,criticality,userRefId,clientRefId,policyRefId,workflowRefId,data`. The range defaults to the last 7 days (at most 400), and the file holds at most 100,000 rows. |

### 2.5 Workflows — `…/workflows`

Authoring and reading definitions. Publishing is versioned: a revision is a new version, never an
in-place edit, which is why there is no update verb.

| Endpoint | Permission | What it does |
|---|---|---|
| `POST /` | `workflows.create` | Publishes a definition — new workflow or a new version of one — after validation. The version is assigned by the database under lock, not by the caller. A failure after the row is inserted rolls the publish back and restores the superseded version. |
| `POST validate` | `workflows.create` | Validates a definition **without publishing**. Returns `IsValid` plus every issue (warnings included) with code, message, `nodeId` and, for a problem with one setting, `field`: its path in the node (`config.chatId`, `config.condition`, `retry.factor`). Besides the graph and the triggers it checks what is filled in on each step: required settings (a client and a command, a recipient, a URL, a variable name, a list to loop over, a workflow to start), ranges (a Delay of 0 to 365 days, a positive wait), expressions that read nothing or hold a broken template, and the workspace's clients (`CLIENT_NOT_FOUND` is an error; a client that is pending, revoked or rejected is the warning `CLIENT_NOT_REGISTERED`). A setting left out of the JSON reads as blank and is listed with the rest, rather than failing the request. Warnings also flag a `$shared.NAME` read of a variable no step sets (`SHARED_VARIABLE_UNDECLARED`), an action whose error port leads nowhere or straight to an End (`ERROR_PATH_ENDS`) and a zero Delay. An invalid definition is a 200 with `IsValid: false`, not an error. |
| `GET /` | `workflows.read` | Lists workflow summaries. Paged. Each carries `triggerKinds`, what can start it: `client` (a client message, held or not), `presence`, `schedule`, `webhook` and `manual`, distinct and in node order, read from the newest version's definition whether or not it is published. Offer Run now only when `manual` is there. `draft` (`revision`, `baseVersion`, `updatedBy`, `updatedAt`) is set when the workflow has unpublished changes, null otherwise. A workflow never published is not listed here; see `GET drafts`. |
| `GET drafts` | `workflows.read` | Every draft in the workspace, newest edit first, without definitions: `refId`, `revision`, `baseVersion` (null for a workflow never published), `currentVersion`, `outdated`, name, description, `updatedBy` (the user's RefId) and `updatedAt`. Not paged. |
| `GET {workflowRef}/draft` | `workflows.read` | The workflow's draft, with its definition and `validation` (what `POST validate` says of it now). `outdated` is true when another version was published since the draft was started (`currentVersion` ≠ `baseVersion`). **404** `WORKFLOW_DRAFT_NOT_FOUND` when there is none. |
| `PUT {workflowRef}/draft` | `workflows.create` | Saves the draft: `{ name, description, definition, expectedRevision }`. `expectedRevision` is the revision last read, or null to start a draft. Every save raises the revision; a draft saved, discarded or published since is **409** `WORKFLOW_DRAFT_CONFLICT`, so one editor never overwrites another unseen. An invalid definition is saved, and its problems come back in `validation`. A draft can start a new workflow under a RefId no version has yet; at most 50 such drafts per workspace (`Workflows:MaxNewDraftsPerWorkspace`, **409** `WORKFLOW_DRAFT_LIMIT_REACHED`), and they don't count against the workflow ceiling. **409** `WORKFLOW_REF_TAKEN` for another workspace's RefId, `WORKFLOW_DELETED` for a deleted one. |
| `DELETE {workflowRef}/draft?revision=` | `workflows.create` | Discards the draft; with `revision`, only that revision, so an edit saved meanwhile is kept (**409** `WORKFLOW_DRAFT_CONFLICT`). **404** when there is none. |
| `POST {workflowRef}/draft/publish` | `workflows.create` | Publishes the draft as the next version: `{ revision, force }`. Validated and registered like `POST /`, and the draft is deleted in the same transaction, at that revision only. When another version was published since the draft was started it is **409** `WORKFLOW_DRAFT_OUTDATED` (checked again under the publish's lock); resend with `force: true` to publish anyway. **409** `WORKFLOW_DRAFT_CONFLICT` when `revision` is not the draft's. Deleting the workflow deletes its draft. |
| `GET {workflowRef}` | `workflows.read` | The current published version. |
| `GET {workflowRef}/versions` | `workflows.read` | Every version, newest first, without definitions: number, status (`Published` or `Deprecated` for the newest, `Superseded` for the rest), name, run count and publish time. **404** for a deleted workflow. |
| `GET {workflowRef}/versions/{version}` | `workflows.read` | A specific historical version. Still readable after the workflow is deleted, so a past run's definition can be shown. |
| `POST {workflowRef}/deprecate` | `workflows.delete` | Marks the definition deprecated and deregisters its triggers, so nothing new fires it. Not a delete — the version history and its runs stay queryable. |
| `DELETE {workflowRef}` | `workflows.delete` | Deletes the workflow, which cannot be undone: it leaves the list and every current-version read (**404**), its triggers and schedules are removed, runs still going are cancelled by the engine shortly after, and its RefId cannot be published again (**409** `WORKFLOW_DELETED`). Past runs stay readable by run. **404** for a workflow that is not in this workspace or is already deleted. |
| `POST {workflowRef}/reinstate` | `workflows.delete` | The inverse of deprecate: re-enables the current version **and re-registers its triggers**, re-seeding schedules from their cron. Rejects a workflow that is already enabled. |
| `POST {workflowRef}/rollback/{version}` | `workflows.create` | Republishes an earlier version's definition as a **new** version — history stays append-only. Validated like any other publish, so rolling back to a definition that predates a validation rule fails rather than reinstating a broken workflow. |
| `GET {workflowRef}/triggers/{nodeId}/recent` | `workflows.read` | Why a trigger did or did not start a run: `recent` lists what it did with each of the latest 20 messages that reached it, newest first (`occurredAt`, `channel`, `messageType`, `outcome`, `correlationKey`, and `runRef` when it started one). `outcome` is `StartedRun`, `Queued`, `Dropped`, `Refused`, `Filtered`, `SecretMismatch` or `Idempotent`, or `Held` when a client trigger with a hold time took a matching message. `hold` is where such a trigger stands (`state` `holding`, `fired` or `clear`, `sinceAt`, `dueAt`, `lastEventAt`), and null for any other trigger. Messages that matched no trigger have no workflow to show them under and are not kept. The engine writes outcomes every two seconds, so the newest can take that long to appear, and an engine that stops abruptly loses what it had not written. An unknown node answers an empty list. **404** for a workflow that is not in this workspace or is deleted. |
| `POST {workflowRef}/test-runs` | `workflows.execute` + `workflows.create` | Starts a **test run**: `{ source: "draft" \| "live", payload, triggerNodeId }`. Runs the draft (including one never published) or the live version, whatever its trigger, from a frozen copy of it, so editing the draft meanwhile changes nothing under the run. `payload` is what the run reads as `trigger`, in the trigger's own shape; `triggerNodeId` may be left out when there is one trigger (**400** `TEST_RUN_TRIGGER_REQUIRED` otherwise). The definition must be valid (**400** `WORKFLOW_VALIDATION_FAILED`). Steps' side effects are real, and shared variables are the live ones. One test run at a time per workflow: **409** `TEST_RUN_ACTIVE` names the one going. The trigger's concurrency policy does not apply, the plan's limit of runs going at once, pooled across the tenant's workspaces, does (**409** `PLAN_RUN_LIMIT_REACHED`), and so do the plan's credits for the month (**409** `PLAN_CREDIT_LIMIT_REACHED`). A test run shares the workflow's run numbers, is marked `isTest`, and is left out of every stats endpoint. **200** `{ runRefId }`. |
| `POST {workflowRef}/runs` | `workflows.execute` | Starts a manual run. Verifies the workflow belongs to the workspace, then relays to the engine. Not every non-start is an error: **200** started (or an idempotent retry, returning the original run), **202** queued behind an active run, **409** dropped by the concurrency policy / no manual trigger / workflow deprecated. A 5xx means the engine itself failed. |

### 2.6 Runs — `…/runs` and `…/workflows/{workflowRef}/runs`

| Endpoint | Permission | What it does |
|---|---|---|
| `GET runs` | `workflows.read` | Lists every run in the workspace, newest first — the "recent activity" view. Filterable by `status`, cursor-paged. `status=Waiting` lists the Running runs whose every live branch is waiting. `isTest=true` lists only test runs, `isTest=false` none of them; both by default. |
| `GET workflows/{workflowRef}/runs` | `workflows.read` | Lists runs of one workflow, filterable by `status` (including `Waiting`, as above) and `isTest`, cursor-paged. A workflow never published lists the test runs of its draft. |
| `GET workflows/{workflowRef}/stats` | `workflows.read` | How a workflow is doing over a window (`from`/`to`, default last 30 days): outcome counts, duration p50/p95/max/avg over completed runs, success rate over *finished* runs (null when nothing has finished), the error codes that actually occur, and the slowest nodes. `counts.active` (runs in flight) is split into `running` (a branch is executing a step) and `waiting` (every live branch is parked on a delay, signal or call); the workspace `GET stats` and its per-workflow rows carry the same split. `days` lists every UTC day the window touches, oldest first and zeros included: `{ date, total, succeeded, failed, cancelled, p50Ms }`, where `failed` is failed + partially failed + out of credits and `p50Ms` is the day's median over completed runs (null when none completed). A run counts on the day it started. The workspace `GET stats` has the same `days`, and each of its `workflows[]` rows has its own for a window of up to 92 days (null beyond, to keep the response small); those leave `p50Ms` null. |
| `GET runs/{runRef}` | `workflows.read` | Run detail with its branches. |
| `GET runs/{runRef}/definition` | `workflows.read` | The definition the run ran. For a test run it is the frozen copy of the draft or live version it started from, with status `Test`; no version lookup returns it. |

Every run, in the lists and on its detail, carries `number`: its place in its workflow's runs (#1, #2,
…), counted across every version and never reused. `OnWorkflowRunStartedEvent` carries it as `runNumber`.
`isTest` says a run is a test run (`OnWorkflowRunStartedEvent` carries it too); test runs are numbered
with the rest.
A waiting run (still Running, every live branch waiting) also carries `waiting: { kind, nodeId,
expiresAt, signalName }`: the bookmark kind (`signal`, `timer`, `http`, `childRunCompleted`,
`commandOutcome`, or null for a wait without one, such as a join), when it ends on its own, and for a
signal the name to send to `POST runs/{runRef}/signals/{signalName}`. A run waiting on several things
names one: a signal first, then the oldest wait.

Each run in either list also carries what a one-line row needs, read for the whole page in one call
(`Run_GetListDetails`), so the console never fetches history per run:

- `trigger`: `{ nodeId, kind, name, summary }`, with `summary` built from the trigger's settings
  ("Client greenhouse-1 sent temperature", "Webhook /water-start", "Schedule 0 6 * * *", "Started by
  hand", "Started by another workflow's run").
- `creditsUsed`: credits charged so far across its steps.
- `failedAt`: for a Failed, PartiallyFailed or Failing run, `{ nodeId, name, kind, errorCode }` of
  its first failed step.
- `endedAt`: for a Succeeded run, the last step it completed: its End step, or the last step of its
  last branch. Null once that step's Info history has been retired.
- `currentStep`: for a live run, the step its most recently moved branch is on, with `attempt` and
  `maxAttempts` while it is being retried ("try 2 of 3").
- `workflowName`: the workflow's name at the run's version.

Step names come from the run's own version, so a step renamed or removed later still reads as it was.
| `GET runs/{runRef}/history` | `workflows.read` | The run's history event stream, oldest first, cursor-paged (`limit` default 200) — the execution trace. |
| `POST runs/{runRef}/cancel` | `workflows.execute` | Requests cancellation with a reason. The engine carries it out, so this answers **202** once the request is on its way and the run reads `Cancelling`, then `Cancelled`, shortly after; poll the run to see it. Cooperative, not immediate. **409** `RUN_NOT_CANCELLABLE` for a run that has already finished, **404** `RUN_NOT_FOUND` for a run that is unknown or another workspace's, **503** `EVENT_BUS_UNAVAILABLE` (with `Retry-After`) when the message broker is unavailable — nothing was sent, so retry. |
| `POST runs/{runRef}/signals/{signalName}` | `workflows.execute` | Delivers a named signal to a parked run. Verifies the run belongs to the workspace, then relays to the engine. |

### 2.7 Shared variables — `…/workflows/{workflowRef}/variables`

Workflow-scoped state that outlives any single run, used to coordinate between them.

| Endpoint | Permission | What it does |
|---|---|---|
| `GET /` | `workflows.read` | The workflow's variables by name, at most 200, as one page (`items`, and `totalCount` for how many there are in all, so a longer list shows as cut): `workflowRefId`, `varName`, `varType`, `valueJson`, `updatedAt`. For the console's `$shared.NAME` picker. 404 `WORKFLOW_NOT_FOUND` for a workflow this workspace does not own. |
| `GET {variableName}` | `workflows.read` | Reads one variable. |
| `PUT {variableName}` | `workflows.execute` | Writes one. Operating state, not definition — hence `execute` rather than an authoring permission. |

### 2.8 Realtime — `/hubs/notifications` (SignalR)

The hub accepts its JWT in the `access_token` query parameter as well as the header, since browser
WebSocket clients cannot set headers. The server closes a connection when its token expires
(`CloseOnAuthenticationExpiration`), so access that ends (sign-out, a revoked session, removal from the
workspace) ends on the live feed within one token lifetime. Clients reconnect with a fresh token and
join their workspaces again; a group joined on the old connection does not carry over.

| Method | Permission | What it does |
|---|---|---|
| `JoinWorkspace(workspaceRef)` | `workspace.join` | Subscribes the connection to the workspace's event feed. Resolves the reference to the internal ID the broadcast group is keyed by, and records the mapping on the connection. |
| `LeaveWorkspace(workspaceRef)` | none | Unsubscribes, using the mapping the join recorded. Deliberately ungated: a permission check here could only fail *after* a successful join — membership removed, or the permission revoked — and failing it would strand the connection in a group it can no longer ask to leave. |
| `JoinClient(workspaceRef, clientRef)` | `workspace.join` + `logs.read` | Subscribes the connection to one client's live traffic: the events marked `NotifyScope.Client` (message received, command, delivered, acked, failed, ping). The same permission as reading its comms history. A client in another workspace fails like one that doesn't exist. |
| `LeaveClient(clientRef)` | none | Stops that client's traffic, using the mapping the join recorded; ungated for the same reason as `LeaveWorkspace`. |

Per-client traffic is **not** sent to the workspace group: only connections that called `JoinClient`
get it, so a tab that isn't on that client's page receives none of it and no backplane publish is
spent on it. Lifecycle events (connected, disconnected, status, rename, latency) stay workspace-wide
because lists show them. The client feed is capped at 20 messages per client per second per
management instance; when messages are dropped, the next second opens with
`OnClientFeedSkipped { clientRefId, skipped, createdAtUtc }`, and the comms history still has them all.
Like workspace groups, client groups don't survive a reconnect: the console joins again.

Because per-client traffic stays off the workspace group, the workspace group gets a rate instead:
every 5 seconds while it is non-zero, and once more when it drops to zero,
`OnWorkspaceMessageRateEvent { inboundPerSecond, outboundPerSecond, messagesPerSecond, windowSeconds, createdAtUtc }`.
Inbound is messages clients sent (including any the 20/s feed cap dropped), outbound is commands
written to a connection. Events more than two windows old when the host reads them are not counted, so
a backlog drained after a restart does not read as a burst. A listener should treat the last value as
stale once about three windows (`windowSeconds` × 3) pass with no event: the final zero can be missed
while a connection is re-establishing. `GET /api/v1/workspaces/{workspaceRef}/stats/messages` (`clients.read`)
returns the last window's value, so a status bar has a number before the first event. Like the cap,
it is counted per management instance: with more than one, each reports its share.

A client whose connections keep replacing each other (5 supersedes within 10 minutes on one gateway)
raises `ClientCredentialsSharedEvent`, a Warning in the event log and `OnClientCredentialsSharedEvent
{ clientRefId, supersedes, windowMinutes, createdAtUtc }` to the client's watchers, at most once per
10 minutes per client. It almost always means two boards are using one set of credentials.

> **Known gap.** The workspace feed is per workspace, not per permission. A member holding only
> `workflows.read` still receives the workspace-wide events. Narrowing it further means
> per-event-category groups.

### 2.9 Client edges (anonymous)

Pre-authentication client flows: a client enrolling or signing in has no token yet. The whole client flow is in [Client.Protocol.md](Client.Protocol.md), and the socket in its [WebSocket binding](Client.Protocol.WebSocket.md). Both are hidden
from the API docs.

In production the `clients` container serves these two paths on the same `api.` host: a management
host running as `Host__Role=ClientAuth`, which serves nothing else. The console API's containers serve
them too, and take over while no client-auth instance is healthy (see deploy/README.md).

| Endpoint | What it does |
|---|---|
| `POST api/v1/client-registrations/initiate` | A client presents a policy PIN and a name; gets back its `RefId` and secret. Auto-approval policies return it registered, otherwise it waits for an operator. |
| `POST api/v1/client-auth/login` | A client exchanges its `RefId` + secret for a short-lived access token, which it then presents to the WebSocket gateway. |

### 2.10 Public workflow callbacks (anonymous)

The front door for the engine's externally-triggered channels. Authorization is possession of the
token or path, which the workflow author defines and hands to whoever is meant to call back —
exactly like a webhook URL.

| Endpoint | What it does |
|---|---|
| `POST api/callbacks/wake/{token}` | Wakes a run parked on a `WaitForHttp` node. |
| `POST api/callbacks/webhook/{workspaceRef}/{path}` | Fires a webhook trigger, which may start a run. An optional `Idempotency-Key` header (1–255 printable characters, else **400**) names the delivery: a retry with the same key, path and secret within 24 hours starts nothing new. Without it every request is a new delivery. |

**202 means safely received.** Management does not call the engine: it checks the request and sends
it to the engine's durable `workflow-inbound-callback` queue, answering once the broker holds it. A
callback that arrives while the engine is restarting or failing over is handled when it is back. Only
when the broker itself does not take it is the answer **503** with `Retry-After: 5`.

Hardened for an anonymous edge, on three axes:

- **Rate limited per target.** A webhook's budget is its workspace and path (600 requests/minute,
  `RateLimiting:Webhook`), not the caller's IP, because LoRaWAN network servers and vendor clouds
  forward many clients, and many workspaces, from a few shared addresses. A wake is limited per
  client IP (60/minute, `RateLimiting:Wake`). Every callback from one IP also shares a ceiling
  (1200/minute, `RateLimiting:PublicCallbacks`) that throttles token and path enumeration and blunts
  floods. Each section takes `PermitLimit` and `WindowSeconds`.
- **Body capped at 128 KiB**, bounding how much attacker-controlled data one call can persist into
  workflow state.
- **Responses are uniform and opaque** — always 202, never the match result — so the response cannot
  be used as an oracle to tell a live token from a dead one. The outcome is logged for operators.

These relay inward to the engine over the backend network with the shared API key attached, so the
engine is never exposed publicly and the manual/signal channels never get a public route at all.

### 2.11 Credits and usage — `…/credit-cap`, `…/usage`

| Endpoint | Permission | What it does |
|---|---|---|
| `GET /api/v1/workspaces/{workspaceRef}/usage` | `billing.read` | What the plan allows against what has been used this calendar month (UTC): `{ plan, periodStart, periodEnd, account, workspace, days }`. `account` is pooled across the account's workspaces, as the limits are: `credits: { used, limit, topUp }` (`topUp` is what bought credit packs still hold; a run is refused once `used` reaches `limit + topUp`, or twice `limit` plus `topUp` on Business, whose runs carry on past the allowance and are invoiced for it), `messages: { used, limit, throttled }`, `clients` and `activeRuns: { used, limit }`. `workspace` is this workspace's share: `credits`, `monthlyCreditCap` (null when uncapped), `messages`, `clients`, `activeRuns`. Messages count both ways: what clients sent and the commands delivered to them. `throttled` is true for a Free account that has used its messages, whose clients may then send one message a minute each until the 1st. Credits count the ledger plus what running runs have not posted yet, as a run start does; clients are approved ones. `days` is this workspace's credits per UTC day from `from` to `to` (default the last 31 days, at most 366), leaving out days with none. |
| `PUT /api/v1/workspaces/{workspaceRef}/credit-cap` | `billing.manage` | Sets the workspace's monthly credit cap, `{ monthlyCreditCap }`, or removes it with null. A ceiling on what it may spend of the account's shared credits in a calendar month (UTC), not a reservation: the caps of an account's workspaces may add up to more than it has. **200** `{ monthlyCreditCap, creditsSpentThisMonth, capReached }`; `capReached` true means the workspace already spent that much, so its new runs are refused from now on. Below 0 or above 10^12 is **400** `WORKSPACE_CREDIT_CAP_INVALID`. Raises `WorkspaceCreditCapSetEvent`. |

A workspace whose spend reaches 80% and then 100% of its cap raises `WorkspaceCreditCapReachedEvent`
(Warning, `OnWorkspaceCreditCapReachedEvent { percent, monthlyCreditCap, creditsSpent, createdAtUtc }`)
once per threshold. A run refused for the cap is **409** `WORKSPACE_CREDIT_CAP_REACHED` on a test run,
and `Refused` (reported with `reason: WorkspaceCreditCap`) for a trigger.

An account found over its monthly messages raises `WorkspaceMessageLimitReachedEvent` once per workspace
and month (Warning, `OnWorkspaceMessageLimitReachedEvent { messagesPerMonth, messagesUsed, throttled, createdAtUtc }`),
within about a minute of the crossing. `throttled` is true on Free; on a paid plan nothing is slowed.

For operators, the Business overage behind the invoice:

| Endpoint | Guard | What it does |
|---|---|---|
| `GET /api/internal/billing/business-overage?year=&month=` | shared internal key | **Backend network only; for operators.** Each Business tenant's use past its contract in that calendar month (UTC), for the invoice: `{ monthStart, tenants: [{ tenantRefId, messagesPerMonth, messagesUsed, extraMessages, creditsPerMonth, creditsUsed, creditsFromPacks, extraCredits, amountUsd }] }`. Credits its packs paid for are not overage. Guarded like the auth host's internal routes: `X-Wbskt-Internal-Key` must match this host's `Internal:ApiKey` (`AUTH_INTERNAL_API_KEY`). `BILLING_MONTH_INVALID` (400) for a month that has not started or does not exist. |

### 2.12 Operational

| Endpoint | Auth | What it does |
|---|---|---|
| `GET /healthz` | anonymous | Liveness — the process is up. Probes nothing, so a dependency failure cannot trigger a restart loop. |
| `GET /healthz/ready` | anonymous | Readiness — dependencies reachable. 503 when not. This is what the compose healthcheck consumes, so it also drives Traefik's routing table and `deploy.sh`'s health wait. |
| `GET /openapi`, `/scalar` | anonymous, **development only** unless `ApiDocs:Enabled` (`API_DOCS_ENABLED`) is true | API docs. Explicitly exempted from the default-deny fallback policy. |

---

## 3. WebSocket gateway

The client data plane. One long-lived WebSocket per client; everything else is a bus event.
`Wbskt.Gateway.WebSocket.Host` is the transport only (the upgrade, the subprotocol, the frame
loop and the close codes); the session logic it runs (the token check and revocation, one connection
per client, the message budget and size limit, command delivery and acks, pings and presence) is
`Wbskt.Gateway.Core`, which later gateways for other transports will share. Clients reach it as
`wss://ws.${DOMAIN}/ws`; the compose service is `gateway-ws`, once called `socket`.

A gateway for another transport takes two calls from Core's `ClientSessions`. `AdmitAsync` reads
the client's identity from its authenticated token and checks it was not revoked since it was issued;
the transport turns a refusal into its own answer. `RunAsync` then takes that admission and runs
the session around the transport's receive loop and close, both passed in, so registering the client, announcing it, routing
its messages and recording its end happen in one order for every transport. The transport implements
`IClientConnection` (sending a text message and closing) and caps each read at 64 KB. An architecture
test keeps WebSocket and HTTP types out of Core.

Some names keep "socket" on purpose:

| Name | Why it stays |
|---|---|
| `wbskt-socket` token audience (`JwtAudiences.Socket`) | It is in every client token already issued. |
| `SocketHostHeartbeat`, `SocketHostPresenceSnapshot`, `SocketHostStartedEvent` | MassTransit names exchanges after message types; renaming one needs management and every gateway deployed together. |
| `wbskt_socket_*` metrics | The `SocketDisconnectRateHigh` alert and the dashboards are built on them. |
| `SOCKET_URL`, `E2E_SOCKET_RESTART_COMMAND` | Client and SDK configuration, and an E2E variable CI and developers set. |

`Gateway:MessageBudget` (once `Socket:MessageBudget`) holds the per-client message budget. A client of a Free account that has used its monthly messages is held, on top of that, to one of its own messages a minute (`sys.*` messages are not held back); the rest are dropped without closing the connection, and counted on the dropped-messages metric as `allowance`.

| Endpoint | Auth | What it does |
|---|---|---|
| `GET /ws` (upgrade) | client JWT, `Authorization: Bearer` **or** `?access_token=` | Establishes the client's connection. The query fallback exists for browser WebSocket clients, which cannot set headers. Refuses before any socket is opened: 401 for a token that does not validate, 401 `CLIENT_TOKEN_INVALID` for one missing a client claim, 403 for a token that is not a client token, 403 `CLIENT_REVOKED` for a revoked client, 400 `SOCKET_PROTOCOL_UNSUPPORTED` and 400 `SOCKET_UPGRADE_REQUIRED`. |
| `GET /healthz` | anonymous | Liveness — the process is up. Probes nothing, so a dependency failure cannot trigger a restart loop. |
| `GET /healthz/ready` | anonymous | Readiness — dependencies reachable. 503 when not. This is what the compose healthcheck consumes, so it also drives Traefik's routing table and `deploy.sh`'s health wait. |

The protocol is versioned by WebSocket subprotocol: a client offers `Sec-WebSocket-Protocol: wbskt.v1`
and the host accepts it. A client that offers none is spoken to as v1; one that offers only other
versions is refused with 400 before the upgrade. Everything a client speaks lives in
`Clients/Wbskt.Protocol`: the message types and which of them are reserved (compared ignoring case,
so `SYS.ping` is as reserved as `sys.ping`), the payloads including `capabilities`, the limits, and
the register and login records of the two HTTP calls before the socket opens. Every host and the
.NET SDK build against it, so there is one copy of the contract.

Once open, the connection carries the platform protocol — `sys.ping { timestamp, id }`/`sys.pong
{ originalTimestamp, id }` (the host times the round trip on its own clock and matches the pong by
`id`; a pong without one answers the pending ping), command frames and their `sys.ack`, state and
capability reports. Commands arrive from the management host over the bus
and are routed by `ClientRefId` to whichever gateway instance holds the connection. Every gateway instance
receives every command; only the one the management host saw holding the connection reports it
missing. A command frame may carry `expiresAt`; an SDK that receives it late answers
`sys.ack { commandId, type, refused: "expired" }` instead of raising it, and the gateway reports
that as a failed command.

---

## 4. Workflow engine host

**Not routed publicly.** Traefik never publishes it; it is reachable only from the backend network,
and every `/api/inbound/*` route additionally requires the shared key in `X-Wbskt-Api-Key`
(fixed-time compared). In production a missing `Engine:InboundApiKey` fails the endpoint closed with
503 rather than running unauthenticated; in development an unset key disables the check.

| Endpoint | What it does |
|---|---|
| `POST api/inbound/manual/{workflowRefId}` | Starts a run. Takes an optional `idempotencyKey` so a retried call does not start a second run. Reached only via the management host's authenticated `POST …/workflows/{refId}/runs`. |
| `POST api/inbound/signal/{scopeRunRefId}/{signalName}` | Delivers a signal to a parked run. Reached only via the management host's authenticated signal route — it has no public front door. |

Unlike the public front door, these return their real outcome, since the caller is a trusted service
and there is no oracle to protect against. Both answer in one shape, `InboundResponse` in
`Wbskt.Workflow.Engine.Contracts`, which the management host reads too: `outcome`, `runRefId` and
`registrations`. Runs are named by RefId only; the engine's row ids never leave it.

A webhook trigger may carry a **secret**. When it does, the caller must present it in the
`X-Wbskt-Secret` header — on the public callback, which passes only its hash to the engine, beside the body (never
in it, since the body becomes the run's persisted trigger payload). The comparison is fixed-time, and the public
response stays an opaque 202 either way, so a wrong secret is indistinguishable from a right one. A
trigger with no secret configured is open, as every webhook published before secrets existed is.

A trigger may also carry a **filter** expression, evaluated against the payload before any run starts;
a non-matching event reports `Filtered` for that registration and starts nothing.

One event can match several trigger registrations — two workflows sharing a webhook path, or one
workflow with two manual triggers — and each of them independently starts, queues or drops a run. The
manual response therefore carries a `Registrations` array (`workflowRefId`, `outcome`, `runRefId`,
`correlationKey`) alongside the top-level `Outcome`
and `RunRefId`, which summarise the *first* started run and stay for callers written against the
single-run shape. A signal's array is always empty: a signal only resumes a waiting run, so it cannot
fan out.

| Endpoint | What it does |
|---|---|
| `GET /healthz` | Liveness — the process is up. |
| `GET /healthz/ready` | Readiness: SQL reachable, bus started, and this instance holds the engine lease. The engine runs active/passive, so only the leader reports ready — that is what keeps a standby out of Traefik's routing table. |
