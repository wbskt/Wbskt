# Plans and billing

How Wbskt knows what each account may use. The build plan and the open work are tracked under #167.

## Plans

A **tenant** (an account) is on one plan. Every limit is pooled across the tenant's workspaces: a Maker
tenant with three workspaces has 100 clients between them, not 100 each.

| Limit | Free | Maker | Pro | Business |
|---|---|---|---|---|
| Price | $0 | $5 / month or $50 / year | $24 / month or $240 / year | $0.15 / client / month, minimum $250 a month |
| Clients | 20 | 100 | 500 | 1,000 |
| Messages a month | 1.5 M | 10 M | 50 M | 100 M |
| Workflow credits a month | 2,000 | 25,000 | 150,000 | 300,000 |
| Runs going at once | 20 | 100 | 200 | 200 |
| Workspaces | 1 | 3 | 10 | 25 |
| Members | 1 | 3 | 10 | 25 |
| Raw readings kept | 3 days | 14 days | 30 days | 30 days |
| Hourly readings kept | 30 days | 1 year | 2 years | 2 years |
| Run history kept | 7 days | 14 days | 30 days | 30 days |

The table is `PlanCatalog.BuiltIn` (`Wbskt.Infrastructure/Billing`). Two layers can change it without a
release, each replacing only the fields it sets:

1. **Configuration**, for every tenant on a plan: `Billing:Plans:{Plan}:{Limit}`, for example
   `Billing:Plans:Free:Clients = 25`. A negative value stops the host at start.
2. **A tenant's overrides**, for a negotiated Business contract: `limitOverrides` on the tenant's plan.

## Where the plan lives

```
auth DB                                   main DB
dbo.Tenants.PlanCode ──(copy)──────────►  dbo.WorkspaceAccounts (one row per workspace)
         PlanLimitOverrides                        │
         PlanRevision                              ▼
                                          IWorkspacePlans  (engine, management, gateway)
```

- **The auth host owns the plan** (`dbo.Tenants`). `PUT /api/internal/tenants/{tenantRef}/plan` changes
  it through `TenantPlanService`, which the payment webhook will call too. A change raises
  `PlanRevision` and writes `TenantPlanChangedEvent` to the outbox in the same transaction
  (`dbo.TenantPlan_Set`). A change worked out from a stale read is refused, and asking for the plan
  already held is a no-op, so a webhook delivered twice is harmless.
- **The main database holds a copy per workspace** (`dbo.WorkspaceAccounts`), because the hosts that
  enforce limits read the main database and must not call the auth host on every client message or
  run start. `TenantId` on the copy is what lets pooled limits be counted there.
- **The management host keeps the copies** (`WorkspaceAccountSync`), always reading the current plan
  from `GET /api/internal/workspaces/accounts` rather than trusting an event's contents:
  - `TenantPlanChangedHandler` re-copies the tenant's workspaces after a plan change;
  - `WorkspaceCreatedAccountHandler` copies a new workspace at once;
  - `WorkspaceAccountSyncWorker` copies every workspace at start and every
    `Billing:AccountSyncInterval` (15 minutes), the backstop for anything an event missed.
  A copy is only replaced by one at the same or a later `PlanRevision` (`dbo.WorkspaceAccount_Upsert`),
  so events and syncs can land in any order.
- **Readers** use `IWorkspacePlans` (`AddWorkspacePlans`), which caches a plan for one minute. A
  workspace with no copy yet is on Free, the plan every tenant starts on. So a plan change reaches
  every host within about a minute of the event being handled.
- Retiring a deleted workspace (`dbo.Workspace_Retire`) removes its copy. A sync page read just before
  the delete can write the copy back; it is harmless, since the workspace holds no live clients or
  runs to count, and costs one row.
