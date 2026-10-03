# API Endpoints

Every HTTP surface in the system, by host. For the permission model behind the slugs — scope
semantics, resolution precedence, where the checks live — see [AccessControl.md](AccessControl.md).

## Hosts at a glance

| Host | Routed publicly | Callers | How a request is authenticated |
|---|---|---|---|
| **Auth** (`Wbskt.Auth.Host`) | yes | console, other hosts | JWT bearer; the credential endpoints are anonymous and rate limited |
| **Management** (`Wbskt.Management.Host`) | yes | console, device SDK | JWT bearer, plus three deliberately anonymous edges (below) |
| **Socket** (`Wbskt.Socket.Host`) | yes | device SDK | JWT bearer on the WebSocket upgrade, header or `access_token` query |
| **Workflow Engine** (`Wbskt.Workflow.Engine.Host`) | **no** — backend network only | management host | shared API key header on `/api/inbound/*` |

**The three publicly routed hosts default-deny.** Management, Auth and Socket each set an
authorization `FallbackPolicy`, so a controller that forgets `[Authorize]` cannot be reached
anonymously and the anonymous surface is an explicit, auditable choice:

- **Management host** — the original; the other two were brought into line with it.
- **Auth host** — every controller was already correctly attributed when the fallback was added, so
  it changed no behaviour. It exists for the next endpoint added to the host that owns tenants,
  roles and permissions. `AuthController`'s credential endpoints carry `[AllowAnonymous]`
  individually.
- **Socket host** — has no controllers at all. `/ws` opts out with `AllowAnonymous` because it
  authenticates itself earlier in the pipeline: `WebSocketAuthMiddleware` validates the token and
  assigns `context.User` before `UseAuthentication` runs, and the browser upgrade path carries its
  token in `?access_token=` rather than a header, so the bearer handler never sees one.
  `EndpointAuthorizationTests` fails if a controller appears here without stating its posture.
- **Engine host** — the exception, and it is not publicly routed. `InboundApiKeyMiddleware` gates
  only endpoints carrying `[InboundEndpoint]`; a controller without that attribute is reachable
  unauthenticated by anything on the backend network.

Two conventions run through every table below:

- **References, never IDs.** Public routes address resources by opaque `Guid` (`RefId`). Internal
  integer IDs never appear in a URL or a response body.
- **A reference that does not resolve is a 403, not a 404**, so the endpoints cannot be used to
  enumerate resources. See "The ID Boundary" in [Coding.Conventions.md](Coding.Conventions.md).

---

## 1. Auth host

### 1.1 Credentials — `api/auth`

Anonymous except where noted, and rate limited per client IP (`RateLimiting:Authentication`) so the
password hasher cannot be used as a work amplifier.

| Endpoint | Auth | What it does |
|---|---|---|
| `POST register` | anonymous | Creates an account **and its own tenant** — the tenant row, its `Admin`/`User` roles, the creator's membership, a tenant-wide Admin assignment and a default workspace. Accepts an optional `invitationToken` to join an existing tenant at the same time. Mails a confirmation link; the account cannot sign in until it is followed. Answers **204 whether or not the address is already registered** — a taken address creates nothing and mails its real owner instead. A taken *username* is still a 409 `AUTH_USERNAME_CONFLICT`: it discloses nothing about any address. |
| `POST login` | anonymous | Exchanges credentials for an access/refresh token pair. Every failure mode answers identically, so the endpoint cannot be used to probe which addresses are registered — except `AUTH_EMAIL_UNVERIFIED`, which is returned only *after* the password verifies and therefore tells a caller nothing they did not already prove. |
| `POST refresh-token` | anonymous | Rotating refresh: issues a new pair and revokes the token presented. Presenting an already-revoked token is treated as a leak — every refresh token for that user is revoked and a `SecurityAlertEvent` is published. |
| `POST logout` | anonymous | Revokes the one refresh token presented. Succeeds whether or not it existed. |
| `POST logout-all` | authenticated | Revokes the caller's entire refresh-token set. |
| `POST change-password` | authenticated | Takes `currentPassword` and `newPassword`. Writes the new password and revokes **every** session the account has in one transaction, then returns a fresh token pair for the caller. A wrong current password is 400 `AUTH_CURRENT_PASSWORD_INVALID` (not 401, so a client does not sign out) and counts towards the lockout below; a locked account is 403 `AUTH_ACCOUNT_LOCKED`. Rate limited like the anonymous endpoints. |
| `GET sessions` | authenticated | The caller's live sessions, newest first: `id`, `createdAt`, `expiresAt`, `createdByIp`. One per signed-in device or browser. |
| `DELETE sessions/{id}` | authenticated | Ends one of the caller's sessions: its refresh token stops working. 404 `AUTH_SESSION_NOT_FOUND` for an id that is not a live session of theirs. Access tokens are not tied to a session, so one already issued to that device lives out its 15 minutes. |
| `POST forgot-password` | anonymous | Mails a reset link if the address has a usable account. **Always 204** — same status, same empty body, for a registered address, an unknown one, a deactivated one, and a malformed one. |
| `POST reset-password` | anonymous | Redeems a reset token, writes the new password, and revokes **every** refresh token the account holds — in one transaction, so a session an attacker already has cannot outlive the recovery. Unknown, spent and expired tokens are all `RESET_TOKEN_INVALID`. |
| `POST verify-email` | anonymous | Redeems a confirmation token and marks the address verified. Single-use. |
| `POST resend-verification` | anonymous | Mails a fresh confirmation link. Anonymous by necessity, not oversight: sign-in requires a confirmed address, so an account that needs this cannot hold a token with which to ask. Always 204, like `forgot-password`. |

Access tokens last 15 minutes (`Jwt:AccessTokenLifetime`); refresh tokens last 7 days. Logout-all,
deactivation, a password reset or change, and a replayed refresh token each revoke the user's refresh tokens
and, through a per-user watermark shared in Redis, the access tokens already issued, on the auth and
management hosts alike. Revoking access tokens is best effort: with Redis unreachable they live out
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

### 1.2 Invitations — `api/invitations`

| Endpoint | Auth | What it does |
|---|---|---|
| `POST accept` | authenticated | Redeems an invitation token into a tenant membership. The account's email must match the address invited, so a leaked link is not by itself enough to join. Every rejection — unknown, expired, revoked, spent, wrong address — is reported identically. |

Issuing invitations lives under the tenant it belongs to (§1.3). Only the SHA-256 hash of a token is
stored; the raw value is returned once, by the call that created it.

### 1.3 Tenant administration — `api/tenants`

All authenticated. Permissions here must be held **tenant-wide** (`WorkspaceId IS NULL`) — a
workspace-scoped grant deliberately does not satisfy them. References resolve tenant-scoped, so a
role or user belonging to another tenant reads as nonexistent.

**Tenants**

| Endpoint | Permission | What it does |
|---|---|---|
| `GET /` | none | Lists the caller's tenants. The entry point for everything below. |
| `POST /` | none | Creates a tenant the caller administers. Self-serve, hence ungated. |
| `PUT {tenantRef}` | `users.manage` | Renames a tenant or changes its description. |

**Invitations**

| Endpoint | Permission | What it does |
|---|---|---|
| `GET {tenantRef}/invitations` | `users.read` | Lists outstanding invitations. |
| `POST {tenantRef}/invitations` | `users.manage` | Issues one against an email address and mails the invitee a link. Optional `roleRef` (granted tenant-wide) and `workspaceRefs` (up to 50 workspaces of this tenant the invitee joins on acceptance; one outside it is 403 `WORKSPACE_NOT_FOUND`). Still returns the raw token **once**, so an administrator can deliver it by hand when mail is not an option. The listing returns each invitation's `workspaceRefs`. |
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
| `DELETE {tenantRef}/roles/{roleRef}/permissions/{slug}` | `roles.manage` | Removes the entry entirely. Distinct from denying it. |

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
| `POST` / `DELETE {tenantRef}/members/{userRef}/groups/{groupRef}` | `users.manage` | Adds or removes a group membership. |
| `POST` / `DELETE {tenantRef}/members/{userRef}/roles/{roleRef}` | `roles.manage` | Assigns or removes a role, scoped tenant-wide or to one workspace. |
| `POST {tenantRef}/members/{userRef}/permissions` | `roles.manage` | Grants or denies a slug directly on the user. A user-level entry outranks anything role-derived. |
| `DELETE {tenantRef}/members/{userRef}/permissions/{slug}` | `roles.manage` | **Deletes the override, does not deny it.** Denying would leave the override in place; deleting returns the decision to the user's roles. Without this an accidental grant could never be undone through the API. |
| `DELETE {tenantRef}/members/{userRef}` | `users.manage` | Offboards a member: removes every assignment scoped to the tenant and transfers workspaces they owned to the caller. Refuses to remove the last administrator, and refuses self-removal (the transfer would have no recipient). |
| `PUT {tenantRef}/members/{userRef}/suspended` | `users.manage` | Body `{ "isSuspended": bool }`. Suspends the member **in this tenant only**: they keep their account, sessions and other tenants, and their assignments here, but hold nothing here until it is lifted. Refuses self-suspension (`AUTH_CANNOT_SUSPEND_SELF`) and leaving the tenant without an administrator. |

**Catalogue**

| Endpoint | Permission | What it does |
|---|---|---|
| `GET {tenantRef}/permissions` | `roles.read` | The permission catalogue, for building a role editor. Read-only by design: the catalogue is code-defined in `Wbskt.Primitives/Constants/Permissions.cs`, and a slug invented at runtime could not gate anything. |

List endpoints take `skip`/`take`, clamped to 200 rather than rejected, and return `X-Total-Count`.

### 1.4 Workspaces — `api/workspaces`

| Endpoint | Permission | What it does |
|---|---|---|
| `POST resolve` | membership only | **The gate every other host depends on.** Resolves a workspace reference to its internal ID plus the caller's effective permission slugs there. The management host calls this before every workspace-scoped request. Answers 403 for both "not a member" and "no such workspace" and 401 only when the caller cannot be identified at all — the management host relies on that distinction, and treats any other status as a server fault. |
| `GET /` | none | Lists workspaces the caller belongs to. |
| `POST /` | none | Creates a workspace in the caller's tenant and makes them its owner. The owner automatically receives the tenant's `Admin` role scoped to it. |
| `PUT {workspaceRef}` | `users.manage` **in that workspace** | Renames a workspace or changes its description. |
| `PUT {workspaceRef}/owner` | `users.manage` in that workspace | Body `{ "userRef": guid }`. Makes another member of the workspace's tenant its owner, adding them to the workspace if needed. The previous owner stays a member and can then be removed. Someone outside the tenant is 404 `USER_NOT_FOUND`. |
| `DELETE {workspaceRef}` | `users.manage` in that workspace | Deletes the workspace and every assignment scoped to it. Resources owned by other services — clients, policies, workflows — are **not** removed; their workspace reference simply stops resolving. |
| `GET {workspaceRef}/members` | `users.read` in that workspace | Lists members. |
| `POST {workspaceRef}/members` | `users.manage` in that workspace | Adds an existing **tenant member** to the workspace. It will not pull in a non-member: doing so used to let anyone with `users.manage` in one workspace capture any account in the system knowing only its email. An unknown address and a non-member are reported identically. |
| `DELETE {workspaceRef}/members/{userRef}` | `users.manage` in that workspace | Removes a member and any assignment scoped to the workspace. The owner cannot be removed. |

### 1.5 Operational

| Endpoint | Auth | What it does |
|---|---|---|
| `GET /healthz` | anonymous | Liveness — the process is up. Probes nothing, so a dependency failure cannot trigger a restart loop. |
| `GET /healthz/ready` | anonymous | Readiness — dependencies reachable. 503 when not. This is what the compose healthcheck consumes, so it also drives Traefik's routing table and `deploy.sh`'s health wait. |
| `GET /openapi`, Scalar reference | anonymous, **development only** | API docs. Anonymous by consequence rather than by declaration — this host has no fallback policy, so an unmarked endpoint is already open. |

---

## 2. Management host

Every workspace-scoped route is `api/workspaces/{workspaceRef:guid}/…` and resolves through
`POST /api/workspaces/resolve` on the auth host before doing anything else. That call is the
membership gate; the permission slug is the second gate.

> **A reference is not a scope.** Resolving `workspaceRef` establishes which workspace the caller is
> acting in. It says nothing about whether the `clientRefId` or `runRefId` in the same route belongs
> to it — each endpoint checks that separately.

### 2.1 Clients — `…/clients`

| Endpoint | Permission | What it does |
|---|---|---|
| `GET /` | `clients.read` | Lists clients, filterable by `status` and `name`. Paged; total in `X-Total-Count`. |
| `GET policy/{policyRefId}` | `clients.read` | The same list narrowed to one registration policy, after verifying the policy belongs to the workspace. |
| `GET {clientRefId}` | `clients.read` | Full client detail: presence, uptime anchor, latency, self-reported SDK metadata and command capabilities. |
| `GET {clientRefId}/state` | `clients.read` | The client's last-known self-reported state variables. |
| `PATCH {clientRefId}/status` | `clients.update` | Approves or revokes a client. Approving enforces the policy's `MaxClients` ceiling. |
| `PATCH status` | `clients.update` | Gives up to 100 clients one status (`{ clientRefIds, status }`), each handled as the single-client endpoint would. Answers 200 with `updated` and `failed` (ref, code, message), so one client that cannot change (the policy is full, or it is not in this workspace) does not stop the rest. Approvals are taken in the order given. |
| `DELETE {clientRefId}` | `clients.manage` | Deletes a client with its capabilities and state, closes its connection and refuses its still-valid token. Its event-log history stays. The device must register again to come back. |
| `POST {clientRefId}/rotate-secret` | `clients.manage` | Replaces the client's secret and returns the new one once (`{ clientRefId, secret }`). The old secret stops working, tokens issued before the rotation are refused, and the live connection is closed. |
| `PATCH {clientRefId}/name` | `clients.update` | Renames a client (1–100 characters). |
| `POST {clientRefId}/command` | `clients.command` | Sends a command to a connected client and returns a `commandId` for correlating the delivery/ack events that follow. Rejects reserved protocol message types and payloads over 32 KiB. Answers 202 whether or not the client is currently connected — delivery is asynchronous. |
| `POST {clientRefId}/ping` | `clients.ping` | Triggers a round-trip latency measurement. |
| `GET {clientRefId}/comms` | `logs.read` | Recent in/out message history, filterable by `direction=in\|out`. Backfills the Live Comms panel before the realtime stream attaches. Uses `logs.read` rather than `clients.read` because it is a projection of the event log — so the client detail page needs both grants to render fully. |

Command and ping publish onto the event bus, and the socket host dispatches on `ClientRefId` alone —
it has no workspace of its own to check against. The controller's ownership check is therefore the
only one in the path.

### 2.2 Registration policies — `…/registration-policies`

A policy is the enrolment ticket a device presents: a PIN, an optional client ceiling, and whether
approval is automatic or manual.

| Endpoint | Permission | What it does |
|---|---|---|
| `GET /` | `policies.read` | Lists policies, filterable by `autoApproval` and `name`, each enriched with its registered and connected client counts. |
| `GET {refId}` | `policies.read` | One policy, after verifying it belongs to the workspace. |
| `POST /` | `policies.manage` | Creates a policy. The enrolment PIN is generated server-side, not supplied by the caller. |
| `PATCH {refId}` | `policies.manage` | Updates name, auto-approval or enabled state. |
| `POST {refId}/disable` | `policies.manage` | Stops the policy accepting new registrations. Already-registered clients are unaffected. |
| `POST {refId}/rotate-pin` | `policies.manage` | Replaces the PIN and returns the policy with the new one. The old PIN stops registering devices; already-registered clients are unaffected. |

### 2.3 Message templates — `…/message-templates`

Saved send-panel payloads, optionally pinned to a policy.

| Endpoint | Permission | What it does |
|---|---|---|
| `GET /` | `templates.read` | Lists templates, optionally filtered by `policyRefId`. |
| `POST /` | `templates.manage` | Creates one. Validates that the payload is JSON under 32 KiB and that the message type is not reserved for the platform protocol. |
| `PUT {refId}` | `templates.manage` | Replaces a template. |
| `DELETE {refId}` | `templates.manage` | Deletes one. |

### 2.4 Event logs — `…/event-logs`

| Endpoint | Permission | What it does |
|---|---|---|
| `GET /` | `logs.read` | The workspace's event log, filterable by `eventName`, `criticality`, `policyRefId` and `clientRefId`. Paged, newest first. |

### 2.5 Workflows — `…/workflows`

Authoring and reading definitions. Publishing is versioned: a revision is a new version, never an
in-place edit, which is why there is no update verb.

| Endpoint | Permission | What it does |
|---|---|---|
| `POST /` | `workflows.create` | Publishes a definition — new workflow or a new version of one — after validation. The version is assigned by the database under lock, not by the caller. A failure after the row is inserted rolls the publish back and restores the superseded version. |
| `POST validate` | `workflows.create` | Validates a definition **without publishing**. Returns `IsValid` plus every issue (warnings included) with code, message and `nodeId`. An invalid definition is a 200 with `IsValid: false`, not an error. |
| `GET /` | `workflows.read` | Lists workflow summaries. Paged. |
| `GET {refId}` | `workflows.read` | The current published version. |
| `GET {refId}/versions` | `workflows.read` | Every version, newest first, without definitions: number, status (`Published` or `Deprecated` for the newest, `Superseded` for the rest), name, run count and publish time. **404** for a deleted workflow. |
| `GET {refId}/versions/{version}` | `workflows.read` | A specific historical version. Still readable after the workflow is deleted, so a past run's definition can be shown. |
| `POST {refId}/deprecate` | `workflows.delete` | Marks the definition deprecated and deregisters its triggers, so nothing new fires it. Not a delete — the version history and its runs stay queryable. |
| `DELETE {refId}` | `workflows.delete` | Deletes the workflow, which cannot be undone: it leaves the list and every current-version read (**404**), its triggers and schedules are removed, runs still going are cancelled, and its RefId cannot be published again (**409** `WORKFLOW_DELETED`). Past runs stay readable by run. **404** for a workflow that is not in this workspace or is already deleted. |
| `POST {refId}/reinstate` | `workflows.delete` | The inverse of deprecate: re-enables the current version **and re-registers its triggers**, re-seeding schedules from their cron. Rejects a workflow that is already enabled. |
| `POST {refId}/rollback/{version}` | `workflows.create` | Republishes an earlier version's definition as a **new** version — history stays append-only. Validated like any other publish, so rolling back to a definition that predates a validation rule fails rather than reinstating a broken workflow. |
| `POST {refId}/runs` | `workflows.execute` | Starts a manual run. Verifies the workflow belongs to the workspace, then relays to the engine. Not every non-start is an error: **200** started (or an idempotent retry, returning the original run), **202** queued behind an active run, **409** dropped by the concurrency policy / no manual trigger / workflow deprecated. A 5xx means the engine itself failed. |

### 2.6 Runs — `…/runs` and `…/workflows/{workflowRefId}/runs`

| Endpoint | Permission | What it does |
|---|---|---|
| `GET runs` | `workflows.read` | Lists every run in the workspace, newest first — the "recent activity" view. Filterable by `status`, cursor-paged. |
| `GET workflows/{workflowRefId}/runs` | `workflows.read` | Lists runs of one workflow, filterable by `status`, cursor-paged. |
| `GET workflows/{workflowRefId}/stats` | `workflows.read` | How a workflow is doing over a window (`from`/`to`, default last 30 days): outcome counts, duration p50/p95/max/avg over completed runs, success rate over *finished* runs (null when nothing has finished), the error codes that actually occur, and the slowest nodes. |
| `GET runs/{runRefId}` | `workflows.read` | Run detail with its branches. |
| `GET runs/{runRefId}/history` | `workflows.read` | The run's history event stream from `fromEventId`, cursor-paged — the execution trace. |
| `POST runs/{runRefId}/cancel` | `workflows.execute` | Requests cancellation with a reason. Cooperative, not immediate. |
| `POST runs/{runRefId}/signals/{signalName}` | `workflows.execute` | Delivers a named signal to a parked run. Verifies the run belongs to the workspace, then relays to the engine. |

### 2.7 Shared variables — `…/workflows/{workflowRefId}/variables`

Workflow-scoped state that outlives any single run, used to coordinate between them.

| Endpoint | Permission | What it does |
|---|---|---|
| `GET {name}` | `workflows.read` | Reads one variable. |
| `PUT {name}` | `workflows.execute` | Writes one. Operating state, not definition — hence `execute` rather than an authoring permission. |

### 2.8 Realtime — `/hubs/notifications` (SignalR)

The hub accepts its JWT in the `access_token` query parameter as well as the header, since browser
WebSocket clients cannot set headers.

| Method | Permission | What it does |
|---|---|---|
| `JoinWorkspace(workspaceRef)` | `workspace.join` | Subscribes the connection to the workspace's event feed. Resolves the reference to the internal ID the broadcast group is keyed by, and records the mapping on the connection. |
| `LeaveWorkspace(workspaceRef)` | none | Unsubscribes, using the mapping the join recorded. Deliberately ungated: a permission check here could only fail *after* a successful join — membership removed, or the permission revoked — and failing it would strand the connection in a group it can no longer ask to leave. |

> **Known gap.** The feed is per workspace, not per permission. A member holding only
> `workflows.read` still receives client command payloads over the hub. Narrowing it means
> per-event-category groups.

### 2.9 Device edges (anonymous)

Pre-authentication device flows: a client enrolling or signing in has no token yet. Both are hidden
from the API docs.

| Endpoint | What it does |
|---|---|
| `POST api/client-registrations/initiate` | A device presents a policy PIN and a name; gets back its `RefId` and secret. Auto-approval policies return it registered, otherwise it waits for an operator. |
| `POST api/client-auth/login` | A device exchanges its `RefId` + secret for a short-lived access token, which it then presents to the socket host. |

### 2.10 Public workflow callbacks (anonymous)

The front door for the engine's externally-triggered channels. Authorization is possession of the
token or path, which the workflow author defines and hands to whoever is meant to call back —
exactly like a webhook URL.

| Endpoint | What it does |
|---|---|
| `POST api/callbacks/wake/{token}` | Wakes a run parked on a `WaitForHttp` node. |
| `POST api/callbacks/webhook/{workspaceRef}/{path}` | Fires a webhook trigger, which may start a run. An optional `Idempotency-Key` header (1–255 printable characters, else **400**) names the delivery: a retry with the same key, path and secret within 24 hours starts nothing new. Without it every request is a new delivery. |

Hardened for an anonymous edge, on three axes:

- **Rate limited per client IP** (60 requests/minute) to throttle token enumeration and blunt floods.
- **Body capped at 128 KiB**, bounding how much attacker-controlled data one call can persist into
  workflow state.
- **Responses are uniform and opaque** — always 202, never the match result — so the response cannot
  be used as an oracle to tell a live token from a dead one. The outcome is logged for operators.

These relay inward to the engine over the backend network with the shared API key attached, so the
engine is never exposed publicly and the manual/signal channels never get a public route at all.

### 2.11 Operational

| Endpoint | Auth | What it does |
|---|---|---|
| `GET /healthz` | anonymous | Liveness — the process is up. Probes nothing, so a dependency failure cannot trigger a restart loop. |
| `GET /healthz/ready` | anonymous | Readiness — dependencies reachable. 503 when not. This is what the compose healthcheck consumes, so it also drives Traefik's routing table and `deploy.sh`'s health wait. |
| `GET /openapi`, `/scalar` | anonymous, **development only** | API docs. Explicitly exempted from the default-deny fallback policy. |

---

## 3. Socket host

The device data plane. One long-lived WebSocket per client; everything else is a bus event.

| Endpoint | Auth | What it does |
|---|---|---|
| `GET /ws` (upgrade) | client JWT, `Authorization: Bearer` **or** `?access_token=` | Establishes the client's connection. The query fallback exists for browser WebSocket clients, which cannot set headers. Rejects the upgrade with 401 before any socket is opened. |
| `GET /healthz` | anonymous | Liveness — the process is up. Probes nothing, so a dependency failure cannot trigger a restart loop. |
| `GET /healthz/ready` | anonymous | Readiness — dependencies reachable. 503 when not. This is what the compose healthcheck consumes, so it also drives Traefik's routing table and `deploy.sh`'s health wait. |

Once open, the connection carries the platform protocol — `sys.ping`/`sys.pong`, command frames and
their `sys.ack`, state and capability reports. Commands arrive from the management host over the bus
and are routed by `ClientRefId` to whichever socket host holds the connection.

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
| `POST api/inbound/wake/{token}` | Wakes a run parked on `WaitForHttp`. Fronted publicly by `api/callbacks/wake/{token}`. |
| `POST api/inbound/webhook/{workspaceRef}/{channelKind}` | Fires a webhook trigger. Fronted publicly by `api/callbacks/webhook/{workspaceRef}/{path}`. |

Unlike the public front door, these return their real outcome (`Outcome`, `Matched`, `RunRefId`) —
the caller is a trusted service, so there is no oracle to protect against.

A webhook trigger may carry a **secret**. When it does, the caller must present it in the
`X-Wbskt-Secret` header — on the public callback, which relays it inward as a header (never in the
body, which becomes the run's persisted trigger payload). The comparison is fixed-time, and the public
response stays an opaque 202 either way, so a wrong secret is indistinguishable from a right one. A
trigger with no secret configured is open, as every webhook published before secrets existed is.

A trigger may also carry a **filter** expression, evaluated against the payload before any run starts;
a non-matching event reports `Filtered` for that registration and starts nothing.

One event can match several trigger registrations — two workflows sharing a webhook path, or one
workflow with two manual triggers — and each of them independently starts, queues or drops a run. The
manual and webhook responses therefore carry a `Registrations` array (`registrationId`,
`workflowRefId`, `outcome`, `runRefId`, `runId`, `correlationKey`) alongside the top-level `Outcome`
and `RunRefId`, which summarise the *first* started run and stay for callers written against the
single-run shape. The signal and wake endpoints have no such array: those channels only resolve
bookmarks, so they cannot fan out.

| Endpoint | What it does |
|---|---|
| `GET /healthz` | Liveness — the process is up. |
| `GET /healthz/ready` | Readiness: SQL reachable, bus started, and this instance holds the engine lease. The engine runs active/passive, so only the leader reports ready — that is what keeps a standby out of Traefik's routing table. |
