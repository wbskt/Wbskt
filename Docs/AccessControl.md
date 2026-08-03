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

- Users are global identities; they belong to tenants via `TenantMembers`, which is many-to-many —
  a user can be in any number of tenants and sees them all through `GET /api/tenants`.
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

## Tenant lifecycle

There is one shape, not two. A single user with several workspaces and an organisation with fifty
members are the same structure at different sizes, so nothing branches on which one a tenant is.

**Registration always creates a tenant** (`dbo.Tenant_Create`), whether or not the user was invited.
It is one transaction because a tenant is unusable without every part of it — the row, its own
`Roles` (roles are tenant-scoped, so a new tenant starts with none), the creator's `TenantMembers`
row, a **tenant-wide** Admin `UserRoles` assignment, and a default workspace. A tenant whose creator
did not end up with tenant-wide `users.manage` cannot be repaired: every management endpoint
requires that permission *in that tenant* to act, so nobody is left who could grant it.

Seeded per tenant, mirroring the post-deployment seed: `Admin` holds every permission in the
catalogue, `User` holds none and is a starting point the administrator customises.

**Joining an existing tenant happens exactly one way — a redeemed invitation.** An administrator
issues one against an email address (`POST /{tenantRef}/invitations`), and the invitee redeems it,
either with an account (`POST /api/invitations/accept`) or while creating one (`invitationToken` on
`POST /api/auth/register`). The accepting account's email must match the address invited, so a
leaked link is not by itself enough to join.

- Only the SHA-256 hash of the token is stored. The raw value is returned once, by the call that
  issued it, and is not recoverable — there is no mail transport in this codebase yet, so the
  administrator delivers the link (`TODO(arch)`).
- `dbo.TenantInvitation_Accept` re-checks validity under `UPDLOCK, HOLDLOCK`, so two concurrent
  redemptions of one token cannot both succeed. Callers may look an invitation up first for a better
  error message, but that read is never the gate.
- Every rejection — unknown token, expired, revoked, spent, wrong address — is reported identically,
  so the endpoint cannot be used to probe for invitations belonging to someone else.

**`WorkspaceMember_Add` no longer joins the tenant implicitly.** It used to insert the missing
`TenantMembers` row, which meant anyone holding `users.manage` in a single workspace could pull any
account in the system into their tenant knowing only its email. It now rejects a user who is not
already a tenant member (`THROW 50009`), and `WorkspaceService` reports that as the same
`USER_NOT_FOUND` as an unknown address, so the endpoint does not reveal whether an address is
registered.

**Leaving:** `DELETE /{tenantRef}/members/{userRef}` removes a member and every assignment scoped to
that tenant, and transfers workspaces they owned to the caller — access resolution gates on a
`WorkspaceMembers` row before evaluating any permission, so a workspace owned by a non-member would
be unreachable to everyone. It refuses to remove the last administrator (`THROW 50008`) and refuses
self-removal, since the transfer would have no recipient. It is distinct from `PUT
/members/{userRef}/active`, which disables the *account* across every tenant it belongs to.

> The last-administrator guard counts direct `UserRoles` and `UserPermissions` only; an
> administrator reached through a group is not counted. That undercounts, so the guard occasionally
> refuses a removal it could safely allow — the deliberate direction to be wrong in, since the
> alternative failure mode leaves a tenant nobody can administer.

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
| Tenants | `GET /api/tenants`, `POST /api/tenants`, `PUT /{tenantRef}` |
| Invitations | `GET/POST /{tenantRef}/invitations`, `DELETE /{tenantRef}/invitations/{invitationRef}`, `POST /api/invitations/accept` |
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

The tenant-lifecycle change needs no migration script: adding `dbo.TenantInvitations`, dropping
`UQ_Tenants_Name` and replacing procedures are all things SqlPackage applies directly. Note that
`dbo.TenantMember_Insert` is gone — it inserted a membership row with no guard at all, which is the
capability the invitation flow exists to replace.
