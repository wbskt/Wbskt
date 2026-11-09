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

## Enforcing the limits

| Limit | Checked in | Refused with |
|---|---|---|
| Clients | `dbo.Client_Create` (registration) and `dbo.Client_UpdateStatuses` (approval) | 50025, **409** `PLAN_CLIENT_LIMIT_REACHED` |
| Runs going at once | `dbo.Run_Start` | 50026; a trigger's dispatch reads `Refused`, a test run is **409** `PLAN_RUN_LIMIT_REACHED` |
| Messages a month | `MessageAllowanceMonitor`, then each socket host | Free only: each client held to one message a minute until the 1st (see Messages) |
| Workflow credits a month | `dbo.Run_Start` | 50033; a trigger's dispatch reads `Refused`, a test run is **409** `PLAN_CREDIT_LIMIT_REACHED` |
| A workspace's credit cap | `dbo.Run_Start` | 50034; a trigger's dispatch reads `Refused`, a test run is **409** `WORKSPACE_CREDIT_CAP_REACHED` |
| Workspaces | `dbo.Workspace_Create` (auth database) | 50030, **409** `PLAN_WORKSPACE_LIMIT_REACHED` |
| Members | `dbo.TenantInvitation_Accept`, and `dbo.TenantInvitation_Create` up front | 50031, **409** `PLAN_MEMBER_LIMIT_REACHED` |

Workspaces and members live in the auth database beside the plan, so the auth host reads the tenant's
limits there (`ITenantLimits`) and the procedure counts under a lock on the tenant's row. A member is
every row of `dbo.TenantMembers`, suspended or not; a pending invitation is not one until it is accepted.

For clients and runs, a caller reads the workspace's plan through `IWorkspacePlans` and passes the limit to the procedure as a
`PlanCeiling` (the limit and the tenant). The procedure counts across `dbo.TenantWorkspaces`, the
workspace itself plus every workspace whose copy names the same tenant, under an application lock from
`dbo.PlanLimitLockResource`: one lock per limit and tenant, so concurrent starts or approvals one short
of the limit in two of a tenant's workspaces cannot both pass. A workspace whose copy has not arrived
has no tenant yet, so it is counted, and locked, on its own against Free's limit until the copy lands.

A limit only refuses what would go past it. After a downgrade, clients and runs already over the new
limit stay as they are; registrations, approvals and starts are refused until the tenant is back under it.

## Credits

A run's steps charge its own counter (`dbo.RunCounters_TryCharge`, against the run's budget), so the hot
path never touches a shared row. The engine's `CreditUsagePoster` (every 10 s, leader only) moves what
each run consumed since it last posted into the ledger, `dbo.WorkspaceCreditUsage` (credits per workspace
per UTC day), through `dbo.RunCredits_Post`. A run's `PostedCredits` moves in the same statement that
reads its delta, so a repeated or retried post adds nothing, and the ledger adds up exactly to what the
runs consumed. `dbo.Run_DeleteRetired` posts anything a run had left before deleting it, so the ledger
outlives the runs.

`dbo.Run_Start` adds up the tenant's ledger for the calendar month (UTC) plus what its running runs have
not posted yet, and refuses the start (50033) once that reaches the plan's `CreditsPerMonth`. Otherwise
the new run's budget is cut to what is left. The check takes no lock: starts racing for the last credits
can overrun by at most the budgets they were given, which together are bounded by what was left times the
runs going at once. A run already going is never stopped for the month's credits; it stops only at its
own budget (`OutOfCredits`). Starts that take a place already held (a sub-workflow child, a queued event's
replay, a run replacing those it cancels) skip the check, as they skip the active-run limit.

**Workspace caps.** An owner can hold one workspace to a monthly credit cap
(`PUT …/workspaces/{workspaceRef}/credit-cap`, `billing.manage`; `dbo.WorkspaceCreditCaps`). Every
workspace still draws on the one shared pool, and a cap is a ceiling rather than a reservation, so the
caps may add up to more than the allowance. `dbo.Run_Start` checks the cap after the tenant's allowance,
counting the workspace's ledger for the month plus its running runs' unposted credits
(`dbo.WorkspaceCreditsSpent`). It refuses with 50034 (**409** `WORKSPACE_CREDIT_CAP_REACHED` on a test
run) and otherwise cuts the run's budget to the smaller of the two remainders. Lowering a cap below what
was already spent refuses new runs at once, and the response says so (`capReached`). As it posts,
`CreditUsagePoster` raises `WorkspaceCreditCapReachedEvent` when a post carries a workspace across 80% and
then 100% of its cap.

A refused trigger is reported once per workflow and limit per minute as `WorkflowTriggerRefusedEvent`,
whose `reason` is `ActiveRunLimit`, `CreditLimit` or `WorkspaceCreditCap`.

**Top-ups.** Credits bought on top of the plan are grants (`dbo.TenantCreditGrants`): a number of credits
for a tenant, spendable until it expires (12 months for a credit pack), recorded once per source and
reference (`TenantCreditsGrantedEvent`, from the auth host's outbox, so a replayed payment adds nothing)
and revoked by a refund (`TenantCreditGrantRevokedEvent`). `dbo.Run_Start` adds what the tenant's live
grants still hold (`dbo.TenantCreditGrantBalance`) to the plan's monthly credits, so a tenant runs past the
plan until the grants are spent too. The plan's credits are spent first: an hour into each month the
management host settles the month before (`CreditGrantSettlement`, `dbo.TenantCreditGrants_Settle`),
drawing what it spent past the plan from the grants that were live in it, soonest to expire first. A month
is settled once (`dbo.TenantCreditGrantSettlements`). Until it is, that month's draw has not left the
balance, so for the first hour of a month a tenant can spend up to its last month's overage twice; the
run budgets bound it as they bound any race for the last credits. A revoked grant's drawn credits stay
drawn; only what it still held stops counting.

## Messages

**The meter.** `dbo.ClientTraffic_InsertBatch`, which the event logger calls with each batch of client
traffic, also counts what it inserted into `dbo.WorkspaceMessageUsage` (per workspace per UTC day): a
client's message (`ClientMessageReceivedEvent`) is inbound, a command delivered to a client
(`ClientCommandDeliveredEvent`) outbound, and the plan's `MessagesPerMonth` counts both. It counts the
rows the insert wrote, in the same transaction, so the meter matches the traffic table and a redelivered
event adds nothing. The meter outlives the traffic's retention.

**The allowance.** The management host's `MessageAllowanceMonitor` (`MessageAllowanceWorker`, every
`Billing:MessageAllowanceInterval`, 1 minute) reads each account's month from the meter, pooled over the
tenant's workspaces (`dbo.WorkspaceMessageUsage_GetAccounts`), and compares it with the plan, overrides
included.

- **Free over its allowance is throttled**, not cut off: each of its clients may send one of its own
  messages a minute until the 1st. The monitor sends every socket host the whole list of throttled
  workspaces each time (`MessageThrottleSnapshot`, not an `IEvent`); the gateway's `MessageThrottle`
  holds it and each session's `ClientAllowance` lets one message a minute through. `sys.*` messages
  (pongs, acks, OTA status) pass, so a throttled client stays connected and keeps answering. A list
  older than 5 minutes is ignored, so a management outage lets messages through rather than throttling
  an account that has since upgraded. The list is empty again on the 1st.
- **A paid plan is never throttled.** What it carries past the limit is overage, read from the same
  meter when top-ups land (#627).
- Either way `WorkspaceMessageLimitReachedEvent` is raised once per workspace and month
  (`dbo.WorkspaceMessageLimit_Record`), and the usage API reports `messages` with `throttled`.

The throttle can trail the crossing by up to a minute plus the event logger's batching, so a Free account
can carry a little past its allowance. Commands sent from the console or the API are not refused: they
are counted when delivered.

## Usage

Two reads show a plan against what has been used, both behind `billing.read` (every member's default):

- **The tenant's seats** on the auth host, `GET /api/v1/tenants/{tenantRef}/usage`: the plan, its limits
  and how many workspaces and members the tenant has, counted as `dbo.Workspace_Create` and the
  invitation procedures count them (`dbo.TenantSeats_GetBy_TenantId`).
- **Credits, messages, clients and runs** on the management host, `GET /api/v1/workspaces/{workspaceRef}/usage`:
  this month's use pooled across the tenant's workspaces (`dbo.TenantWorkspaces`, as the limits are
  checked), this workspace's share and cap, and its credits per day from the ledger
  (`dbo.WorkspaceUsage_GetSummary`, `dbo.WorkspaceCreditUsage_GetDaily`). Credits are counted as
  `dbo.Run_Start` counts them, so a run is refused exactly when `used` reaches `limit`.

The split follows where the data lives: seats are counted in the auth database, everything else in the
main one, and neither host calls the other to answer. Messages are on the management read, from the
message meter (see Messages).

## Payments

Money is taken by a payment provider on its own hosted pages; Dodo Payments is the first
(`DodoPaymentGateway`, behind `IPaymentGateway`). The auth host owns the plan, so payments live there.
Payments are off until the provider's API key and webhook signing secret are set
(`Billing__Dodo__ApiKey`, `Billing__Dodo__WebhookSecret`, or `DODO_PAYMENTS_API_KEY` and
`DODO_PAYMENTS_WEBHOOK_KEY`); the products sold are `Billing:Payments:Plans:{Plan}:{Interval}` and
`Billing:Payments:CreditPacks:{Name}` (`ProductId`, `Credits`). The secrets come from the environment,
never from a file in the repository.

- **Buying.** `POST /api/v1/tenants/{tenantRef}/billing/checkout` (`billing.manage`) names a plan and
  interval or a credit pack, and answers the provider's checkout page. The checkout carries the tenant's
  reference in the provider's metadata (`tenant_ref`) and is paid by the signed-in user's email. A tenant
  whose subscription is still billing changes or cancels it in the provider's portal
  (`POST …/billing/portal`) rather than starting a second one (409 `BILLING_SUBSCRIPTION_ACTIVE`).
  Nothing changes until the provider confirms the payment.
- **Webhooks.** The provider posts to `/api/callbacks/billing/{provider}` (anonymous, unversioned, on
  api.DOMAIN and auth.DOMAIN). The Standard Webhooks signature over the raw body is checked first, with
  5 minutes' tolerance on its timestamp. Each event is stored once per provider and event id
  (`dbo.BillingWebhookEvents`) before it is acted on: a delivery of one already acted on is answered
  without acting again, and one that failed is acted on again when the provider redelivers it. An event
  only says which subscription or payment to read: `TenantBillingSync` reads it from the provider, so a
  late, repeated or reordered delivery cannot move a tenant to a plan it no longer pays for.
- **Subscriptions.** The subscription as read is stored (`dbo.TenantSubscriptions`, one per tenant) and
  decides the plan (`SubscriptionPlan.Decide`): active, past due or on hold for under 7 days
  (`Billing:Payments:PastDueGrace`), or cancelled but inside the period paid for, keeps the paid plan;
  failed, expired, paused, past the grace period or past a cancelled period puts the tenant on Free; a
  pending one changes nothing. The plan moves through `ITenantPlanService.SetAsync`, so its overrides are
  kept and `TenantPlanChangedEvent` reaches the management host as for any plan change. An ended
  subscription's events do not replace a newer live one. Lapses with no webhook (a grace period running
  out, a cancelled period ending) are caught by `SubscriptionReviewWorker`, hourly, which reads each
  such subscription again before acting.
- **Credit packs.** A one-time payment for a credit pack is recorded once per payment
  (`dbo.TenantCreditPurchases`), and the same transaction writes `TenantCreditsGrantedEvent` to the
  outbox; the grant is spendable for `Billing:Payments:CreditPackValidity` (365 days). A full refund
  writes `TenantCreditGrantRevokedEvent` once (see Top-ups under Credits).
- **Refunds of a subscription payment** withhold the paid plan until the subscription has been paid past
  the refunded period. A partial refund changes nothing.
- **Reading.** `GET /api/v1/tenants/{tenantRef}/billing` (`billing.read`) answers the plan, the
  subscription (status, period end, whether it ends there, when it fell past due) and what is on offer.
  Offers are empty while payments are off.

An operator can still set any plan through `PUT /api/internal/tenants/{tenantRef}/plan`; a tenant's next
subscription event puts it back on the plan its subscription pays for.

Lock order, where a procedure takes more than one: the trigger key lock, then the test-run lock, then the
plan lock (`dbo.Run_Start`); the clients' and policies' rows, then the plan lock (client procedures).
