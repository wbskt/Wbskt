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
| Role/group assignment APIs | `POST|DELETE /api/management/users/{userId}/roles/{roleId}` and `.../groups/{groupId}/roles/{roleId}` (`tenantId` required, `workspaceId` optional) |

## Management API gating

`api/management/*` endpoints require a **tenant-wide** permission in the target tenant:
`roles.manage` for role/permission operations, `users.manage` for group/user-group operations.
`POST /api/workspaces/{ref}/members` requires `users.manage` **in that workspace**.
Workspace owners automatically receive the tenant's `Admin` role scoped to the new workspace
(`Workspace_Create`).

## Deploying schema upgrades to an existing DB

Existing databases (with workspace rows) must run
`Databases/Migrations/PreTenantUpgrade.Auth.sql` once **before**
`sqlpackage /Action:Publish` — the new `Workspaces.TenantId` FK validates before the
post-deployment seed can create the default tenant. Fresh deployments (`Deploy-Databases.ps1 -Fresh`)
need nothing extra.
