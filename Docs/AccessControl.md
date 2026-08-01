# Access Control (RBAC)

> Supersedes parts of `access_control.png` — the resolution *precedence* in that diagram is still
> accurate, but the diagram predates tenants and workspace scoping.

## Hierarchy

```
Tenant
 └── Workspace (n per tenant)
      └── WorkspaceMembers (pure membership gate — no role byte)

Tenant-owned definitions:  Roles, Groups (nested via ParentGroupId)
Global definitions:        Permissions (code-defined catalog, Wbskt.Primitives/Constants/Permissions.cs)
```

- Users are global identities; they belong to tenants via `TenantMembers`.
  Registration currently auto-joins the default tenant (Id = 1).
- `RolePermissions` (role → permission, allow/deny) is part of the **role definition** and is unscoped.
- Scoping happens at **assignment**: `UserRoles`, `GroupRoles`, `UserPermissions` each carry
  `TenantId` (required) + `WorkspaceId` (nullable).

## Scope semantics

An assignment **applies** to a check for user `U` in workspace `W` of tenant `T` iff:

```
assignment.TenantId = T AND (assignment.WorkspaceId IS NULL OR assignment.WorkspaceId = W)
```

`WorkspaceId = NULL` means **tenant-wide** (applies in every workspace of that tenant).
Tenant-level checks (management APIs) pass `WorkspaceId = NULL`, so only tenant-wide
assignments count there.

## Resolution precedence (per permission)

Scope only filters which assignments apply — precedence is unchanged from the original design:

```
1. User has explicit DENY   -> DENY
2. User has explicit ALLOW  -> ALLOW
3. Resolve effective roles  =  direct UserRoles  ∪  GroupRoles over the user's groups + ancestor groups
4. Any role has DENY        -> DENY
5. Any role has ALLOW       -> ALLOW
6. Default                  -> DENY
```

Consequences worth knowing:
- A **tenant-wide user DENY** beats a **workspace-scoped role ALLOW** (user level wins).
- A direct **user ALLOW** beats any **role DENY** (unchanged from the previous system).
- Membership in `WorkspaceMembers` is a hard gate *before* any permission evaluation.
- The effective-set formula: `Effective(P) = NOT UserDeny(P) AND (UserAllow(P) OR (NOT RoleDeny(P) AND RoleAllow(P)))`

## Where the logic lives

| Concern | Location |
|---|---|
| Effective permission set for a workspace | `Databases/Wbskt.Database.Auth/StoredProcedures/Permission_EffectiveSet.sql` |
| Single-permission check (scoped) | `.../Permission_Verify.sql` (`@WorkspaceId NULL` = tenant-wide only) |
| Membership gate + set resolution | `Hosts/Wbskt.Auth.Host/Services/WorkspaceService.cs` (`ResolveAccessAsync`) |
| Resolve API | `POST /api/workspaces/resolve` → `{ workspaceId, permissions[] }` |
| Consumer client | `Hosts/Wbskt.Management.Host/Services/Clients/IAuthServiceClient.cs` (`WorkspaceAccess`, single/multi-permission overloads) |
| Tenant administration | `Hosts/Wbskt.Auth.Host/Services/ManagementService.cs` — owns the permission gate and the tenant-scoped reference resolution |

## Tenant administration API

Everything lives under `api/tenants/{tenantRef}` and is addressed by opaque `Guid` reference, never
by internal integer ID. `GET /api/tenants` lists the caller's tenants and is the entry point.

Reference resolution is **tenant-scoped**, which is why it sits in `ManagementService` rather than
the controller: a role, group or user reference belonging to another tenant simply does not resolve,
so a cross-tenant reference is reported the same as one that does not exist. Resolving a user is
additionally gated on their `TenantMembers` row, so an administrator cannot pull an outsider into
their tenant's permission graph.

| Area | Endpoints |
|---|---|
| Roles | `GET/POST /roles`, `PUT/DELETE /roles/{roleRef}`, `GET/POST /roles/{roleRef}/permissions`, `DELETE /roles/{roleRef}/permissions/{slug}` |
| Groups | `GET/POST /groups`, `PUT/DELETE /groups/{groupRef}`, `GET /groups/{groupRef}/roles`, `POST/DELETE /groups/{groupRef}/roles/{roleRef}` |
| Members | `GET /members`, `GET /members/{userRef}/roles\|permissions\|groups`, `POST/DELETE /members/{userRef}/roles/{roleRef}`, `POST /members/{userRef}/permissions`, `DELETE /members/{userRef}/permissions/{slug}`, `POST/DELETE /members/{userRef}/groups/{groupRef}`, `PUT /members/{userRef}/active` |
| Catalogue | `GET /permissions` |

Assignment scope travels in the body as `workspaceRef` on POST (null = tenant-wide) and as a query
parameter on DELETE.

Removing a direct user permission is a **delete, not a deny**. The two are not equivalent: a
user-level row wins over any role-derived permission, so denying leaves the override in place while
deleting returns the decision to the user's roles. Without the delete an accidental grant could
never be undone through the API.

List endpoints take `skip`/`take`, clamped to 200 rather than rejected.

These endpoints require a **tenant-wide** permission in the target tenant:
`roles.read`/`roles.manage` for roles, permissions and assignments; `users.read`/`users.manage` for
groups, membership and account state. The read/manage split exists so that a console which only
displays the permission graph does not need the right to rewrite it.
`POST /api/workspaces/{ref}/members` requires `users.manage` **in that workspace**, and also adds the
user to the workspace's tenant (`WorkspaceMember_Add`) — without that row they would pass the
membership gate but resolve no roles, since every assignment is filtered by `TenantId`.
Workspace owners automatically receive the tenant's `Admin` role scoped to the new workspace
(`Workspace_Create`).

The permission catalogue is **code-defined**: `Wbskt.Primitives/Constants/Permissions.cs` seeded by
`Databases/Wbskt.Database.Auth/Scripts/Script.PostDeployment.sql`. There is deliberately no API to
create permissions — a slug invented at runtime cannot gate anything, because every check in the
codebase is a compile-time `Permissions.X` constant.

## Sessions and tokens

- Access tokens last 60 minutes and are not revocable; refresh tokens last 7 days and are.
- Refresh is **rotating**: `POST /api/auth/refresh-token` issues a new pair and revokes the token
  presented, recording the replacement in `ReplacedByToken`.
- Presenting an **already-revoked** token is treated as a leak: every refresh token for that user is
  revoked and a `SecurityAlertEvent` is published. A client that loses a refresh race is signed out
  rather than left sharing a live token with an attacker.
- `POST /api/auth/logout` revokes one token; `POST /api/auth/logout-all` revokes the caller's whole
  set. Both succeed regardless of whether the token existed, so neither can be used to probe.
- `PUT /api/tenants/{tenantRef}/members/{userRef}/active` disables an account and revokes its refresh tokens.
  An access token already issued stays valid until it expires — deactivation is not instant.
- The credential endpoints are rate limited per client IP (`RateLimiting:Authentication`).

## Error semantics

`ErrorType.Forbidden` → **403** is used for "authenticated but not permitted": failing a management
permission gate, not being a workspace member, or touching a resource in another workspace.
**401** is reserved for "we cannot identify the caller" — bad credentials, or an invalid, expired or
revoked token. Consumers such as `AuthServiceClient` rely on the distinction.

## Deploying schema upgrades to an existing DB

Run these against the Auth DB once, in order, **before** `sqlpackage /Action:Publish` (or before
`Deploy-Databases.ps1` without `-Fresh`). Fresh deployments (`-Fresh`) need neither.

1. `Databases/Migrations/PreTenantUpgrade.Auth.sql` — the `Workspaces.TenantId` FK validates during
   schema deployment, before the post-deployment seed can create the default tenant.
2. `Databases/Migrations/AddRoleGroupRefIds.Auth.sql` — adds `RefId` to `Roles` and `Groups` as a
   nullable column, backfills it, then tightens it to `NOT NULL` with a unique constraint. Doing
   that in one publish against tables that already have rows is a change SqlPackage will refuse or
   attempt as a table rebuild.

Both scripts are idempotent and live outside the `.sqlproj` directory on purpose — the DACPAC build
sweeps up any `.sql` file inside the project folder into the model.
