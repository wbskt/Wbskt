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
- **Every plan records who set it** (`PlanSource`): `Default` (Free, never paid for), `Subscription`
  (a paid subscription's plan) or `Operator` (set by an operator, optionally until `PlanEndsAt`). An
  operator's plan outranks a subscription while it holds: billing leaves it alone until its end date
  passes, and then puts the tenant back on whatever its subscription pays for (Free when nothing).
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
| Messages a month | `MessageAllowanceMonitor`, then each socket host | Free at 1×, Maker and Pro at 2×, Business never: each client held to one message a minute until the 1st (see Messages) |
| Workflow credits a month | `dbo.Run_Start` | 50033; a trigger's dispatch reads `Refused`, a test run is **409** `PLAN_CREDIT_LIMIT_REACHED` |
| A workspace's credit cap | `dbo.Run_Start` | 50034; a trigger's dispatch reads `Refused`, a test run is **409** `WORKSPACE_CREDIT_CAP_REACHED` |
| (a start that waits too long for a lock) | `dbo.Run_Start` | 50035; retried, then a trigger's event is redelivered and a test run is **503** `WORKSPACE_BUSY` |
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

`dbo.Run_Start` works out what the tenant has left of its credits for the calendar month (UTC): the plan's
`CreditsPerMonth` less what the plan itself has covered (the ledger less what was drawn from grants as it
was posted, `dbo.TenantCreditMonths`), never below zero, plus what the tenant's grants still hold, less
what its running runs consumed and have not posted yet. It refuses the start (50033) when nothing is
left, and otherwise cuts the new run's budget to what is. The check takes no lock: starts racing for the
last credits can overrun by at most the budgets they were given, which together are bounded by what was
left times the runs going at once. A run already going is never stopped for the month's credits; it stops
only at its own budget (`OutOfCredits`).

Starts that take a place already held (a queued event's replay, a run replacing those it cancels) skip
the limit on runs going at once, but not the month's credits: a replay after the credits ran out is
refused like any other start. A sub-workflow child is not checked at all. It spends from its root run's
budget (`dbo.RunCounters.BudgetRunId`): its charges add to its own `CreditsConsumed`, which is what is
posted, and to the root's `ChildCreditsConsumed`, and `dbo.RunCounters_TryCharge` holds the root's own
and its children's charges together to the root's budget. A parent is never failed halfway because the
month ran out under its child.

**Workspace caps.** An owner can hold one workspace to a monthly credit cap
(`PUT …/workspaces/{workspaceRef}/credit-cap`, `billing.manage`; `dbo.WorkspaceCreditCaps`). Every
workspace still draws on the one shared pool, and the caps may add up to more than the allowance.
`dbo.Run_Start` checks the cap after the tenant's allowance and holds it exactly: under an application
lock per workspace (`credit-cap:{workspaceId}`), it counts what the workspace spent this month, posted or
not (`dbo.WorkspaceCreditsSpent`), plus the unspent budget of each of its running root runs
(`dbo.WorkspaceCreditsReserved`), and refuses with 50034 (**409** `WORKSPACE_CREDIT_CAP_REACHED` on a test
run) when what is left would not cover the start's own cost. Otherwise it cuts the run's budget to the
smaller of the two remainders. That remainder is the run's ceiling, not its budget: so that one run
going does not hold the whole cap away from the next, a capped run's budget is leased, a twentieth of the
cap (or its start cost, if more) at a time (`dbo.RunCounters.BudgetCeiling`). When a charge would pass the
lease, `dbo.RunCounters_TryCharge` takes the same lock and extends it by what the charge needs (at least
another twentieth), up to the ceiling and what the cap still has free, or refuses the charge
(`OutOfCredits`) and takes nothing. A run cannot charge past its budget, so the workspace cannot pass its
cap, however many runs it starts at once. With the cap removed, a lease extends straight to its ceiling. Lowering a cap below what was already spent refuses new runs at
once, and the response says so (`capReached`). As it posts, `CreditUsagePoster` raises
`WorkspaceCreditCapReachedEvent` when a post carries a workspace across 80% and then 100% of its cap.

**Busy.** Both locks a start can wait on (the tenant's run limit and a workspace's cap) give up after five
seconds with 50035 rather than hold an inbound request and a pooled connection. (A lease extension, which
is not on an inbound request, waits up to 30 seconds for the cap's lock before it fails its step with
50035.) `RunStarter` tries again
twice (after 50 ms, then 200 ms), then throws `RunStartBusyException`: a trigger's event goes back to the
queue to be redelivered, and a test run answers **503** `WORKSPACE_BUSY` with `Retry-After`. A busy start
is never read as a full plan. Each one counts on `wbskt_workflow_run_starts_busy_total`
(`outcome` is `retried` or `gave_up`).

A refused trigger is reported once per workflow and limit per minute as `WorkflowTriggerRefusedEvent`,
whose `reason` is `ActiveRunLimit`, `CreditLimit` or `WorkspaceCreditCap`.

**Top-ups.** Credits bought on top of the plan are grants (`dbo.TenantCreditGrants`): a number of credits
for a tenant, spendable until it expires (12 months for a credit pack), recorded once per source and
reference (`TenantCreditsGrantedEvent`, from the auth host's outbox, so a replayed payment adds nothing)
and revoked by a refund (`TenantCreditGrantRevokedEvent`). `dbo.Run_Start` adds what the tenant's live
grants still hold (`dbo.TenantCreditGrantBalance`) to the plan's monthly credits, so a tenant runs past the
plan until the grants are spent too. The plan's credits are spent first, and the grants are drawn as the
credits are posted: `dbo.RunCredits_Post` (and `dbo.Run_DeleteRetired`, which posts a deleted run's last
credits) calls `dbo.TenantCreditGrants_Draw` with what each tenant posted. It draws only the part of the
post that falls past the plan's allowance (`WorkspaceAccounts.CreditsPerMonth`, from the catalogue) in
force at that moment, from the grants live then (not revoked, held or expired), soonest to expire first,
and records the month's posted and drawn totals in `dbo.TenantCreditMonths`. A month's posted total is
seeded from the ledger on the tenant's first post in it, so it is right after a deploy mid-month. A
downgrade draws only from what is posted after it, and a grant that expired is never drawn from, so the
balance a start reads is always current. A revoked grant's drawn credits stay drawn; only what it still
held stops counting.

**Reconcile.** Grants reach the main database only through those events, so the management host's
`CreditGrantReconcileWorker` (every `Billing:CreditGrantReconcileInterval`, 1 hour) reads every credit pack
bought from the auth host (`GET /api/internal/billing/credit-purchases`, `dbo.TenantCreditPurchase_GetPage`)
and makes the writes an event would have: the grant, its revocation and its latest hold or release. Each
write is idempotent, so the reconcile and the events can meet in any order. Each repair is logged and
counted on `wbskt_management_credit_grants_repaired_total{change}`, which the `CreditGrantsRepaired`
alert watches.

A grant whose payment has an open chargeback is **held** (`TenantCreditGrantHoldChangedEvent`,
`dbo.TenantCreditGrant_SetHold`): it stops counting towards the balance and is not drawn from while held,
and counts again once the chargeback is won or withdrawn. A lost chargeback revokes it as a refund does.
Each hold and release carries the purchase's `HoldRevision`, and the main database applies only a newer
one, so a hold delivered after its release changes nothing.

## Run history

A finished run, with its branches and history, is kept for the plan's `RunHistoryDays` (a tenant's
`runHistoryDays` override wins), counted from when it finished. A workspace with no copy of its
account yet loses no runs to its plan (only to the 365-day window below), and a downgrade keeps the old
plan's days for a while (see Downgrades below).
The engine's `HistoryRetentionGc` (hourly, leader only) hands every plan's days to
`dbo.Run_DeleteRetired`, which works out each workspace's cutoff and deletes the runs past it, posting
any credits they had not posted yet first. A run older than `HistoryRetentionWindowElevated` (365 days)
goes whatever the plan says. Run stats over a range longer than the plan keeps count only the runs
still kept; credit usage is read from the ledger, so it is not affected.

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
  holds it and lets each client one message a minute through, kept by client id so reconnecting
  does not earn a fresh one. `sys.*` messages
  (pongs, acks, OTA status) pass, so a throttled client stays connected and keeps answering. A list
  older than 5 minutes is ignored, so a management outage lets messages through rather than throttling
  an account that has since upgraded. The list is empty again on the 1st.
- **Maker and Pro carry on past their allowance**, with no overage charge, so a customer never sees a
  surprise bill. Only at `Billing:PaidMessageThrottleMultiple` (2) times it are they throttled the same
  way as Free, which stops one account loading the hosts without end. The multiple is a setting so
  pricing can move it.
- **Business is never throttled**; overage is a matter for its contract.
- Each account is told once per workspace and month (`dbo.WorkspaceMessageLimit_Record`, keyed by the
  share): `WorkspaceMessageLimitApproachingEvent` at 80% (skipped when a check finds it already past
  100%), `WorkspaceMessageLimitReachedEvent` at 100% (`throttled` true on Free), and
  `WorkspaceMessagesThrottledEvent` when a paid plan reaches its throttle. The usage API reports
  `messages` with `throttled`. Emailing these is a separate consumer.

The throttle can trail the crossing by up to a minute plus the event logger's batching, so an account
can carry a little past where it is throttled. Commands sent from the console or the API are not refused: they
are counted when delivered.

## Readings

Every number a client reports in a `state.report` is a reading (`dbo.ClientReadings`), kept as sent for
the plan's `RawReadingsDays` and then as an hourly roll-up (`dbo.ClientReadingsHourly`: count, min, max
and sum per client, variable and hour of the client's time) for its `HourlyReadingsDays`. A tenant's
overrides of either apply. The management host's `ClientReadingRetentionService` (every
`Readings:RetentionInterval`, 1 hour) runs `dbo.ClientReadings_Compact` in batches:

- raw readings received before the plan's raw cutoff are deleted and added to their hour in the same
  transaction, so each reading is counted in exactly one table, and a reading arriving late for an hour
  already rolled up adds to it;
- hourly rows older than the plan's hourly cutoff are deleted;
- a deleted client's raw and hourly readings are cleared. `dbo.Client_Delete` queues the client
  (`dbo.DeletedClientReadings`) rather than deleting its history in its own transaction, and a report
  from a client deleted meanwhile is dropped.

Charts (`GET …/clients/{clientRef}/readings`) read both tables together, so a bucket's count, min, max and
average are exact across the boundary; a rolled-up hour lands whole in the bucket its start falls in.
CSV export reads raw readings only. Both tables and their indexes are page compressed. A workspace with
no account copy yet is skipped, so it loses nothing until its plan is known. Moving to a longer plan
keeps what is still there; nothing already rolled up comes back.

**Downgrades.** Retention sweeps cannot be undone, so a plan that lapses for a day must not cost a
customer their history. Each account copy carries its retention days, worked out from the catalogue
with the overrides applied (`WorkspaceAccounts.RawReadingsDays`, `HourlyReadingsDays`,
`RunHistoryDays`). When a copy moves to shorter retention, `dbo.WorkspaceAccount_Upsert` keeps the days
it had, or the longer hold still running from an earlier downgrade, in the `Held*` columns until
`RetentionHeldUntil`, `Billing:RetentionGraceDays` (30) later. Until then both sweeps keep the longer
of the two.

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
  A plan checkout stays open for `Billing:Payments:CheckoutReuseWindow` (30 minutes,
  `dbo.TenantCheckouts`): asking again for the same product in that time, from another tab or another
  admin, answers the same page rather than a second checkout that could be paid as well.
  Nothing changes until the provider confirms the payment.
- **Webhooks.** The provider posts to `/api/callbacks/billing/{provider}` (anonymous, unversioned, on
  api.DOMAIN and auth.DOMAIN). The Standard Webhooks signature over the raw body is checked first, with
  5 minutes' tolerance on its timestamp. Each event is stored once per provider and event id
  (`dbo.BillingWebhookEvents`) before it is acted on: a delivery of one already acted on is answered
  without acting again, and one that failed is acted on again when the provider redelivers it. An event
  only says which subscription or payment to read: `TenantBillingSync` reads it from the provider, so a
  late, repeated or reordered delivery cannot move a tenant to a plan it no longer pays for.
- **Subscriptions.** Every subscription as read is stored (`dbo.TenantSubscriptions`, keyed by provider
  and subscription id). The one that decides the plan (`SubscriptionPlan.Deciding`) is the oldest still
  billing that is not a duplicate, else the newest. Its status decides the plan (`SubscriptionPlan.Decide`): active, past due or on hold for under 7 days
  (`Billing:Payments:PastDueGrace`), or cancelled but inside the period paid for, keeps the paid plan;
  failed, expired, paused, past the grace period or past a cancelled period puts the tenant on Free; a
  pending one changes nothing. The plan moves through `ITenantPlanService.SetAsync`, so its overrides are
  kept and `TenantPlanChangedEvent` reaches the management host as for any plan change. An ended
  subscription's events do not replace a newer live one. Lapses with no webhook (a grace period running
  out, a cancelled period ending) are caught by `SubscriptionReviewWorker`, hourly, which reads each
  such subscription again before acting. The worker runs with payments off too, applying the stored
  copy, so an operator's plan still ends on time.
- **A second subscription paid for anyway** (two checkouts paid at once) is a duplicate: it is marked
  (`dbo.TenantSubscription_MarkDuplicate`), cancelled with the provider, and its payment refunded in full
  once (`dbo.TenantSubscription_ClaimDuplicateRefund`; a refused refund releases the claim, so the next
  event asks again). The tenant keeps the plan of its first subscription.
- **Chargebacks.** A payment whose dispute is open puts a credit pack on hold (see Credits); a lost
  dispute counts as a full refund, for a pack and for a subscription payment. A won or withdrawn dispute
  changes nothing.
- **Credit packs.** A one-time payment for a credit pack is recorded once per payment
  (`dbo.TenantCreditPurchases`), and the same transaction writes `TenantCreditsGrantedEvent` to the
  outbox; the grant is spendable for `Billing:Payments:CreditPackValidity` (365 days). A full refund
  writes `TenantCreditGrantRevokedEvent` once (see Top-ups under Credits).
- **Refunds of a subscription payment** withhold the paid plan until the subscription has been paid past
  the refunded period. A partial refund changes nothing.
- **Reading.** `GET /api/v1/tenants/{tenantRef}/billing` (`billing.read`) answers the plan, the
  subscription (status, period end, whether it ends there, when it fell past due) and what is on offer.
  Offers are empty while payments are off.

An operator sets a plan through `PUT /api/internal/tenants/{tenantRef}/plan`, optionally with `endsAt`.
It is recorded as an `Operator` plan and outranks any subscription until `endsAt` passes (never, without
one); `SubscriptionReviewWorker` then puts the tenant back on what its subscription pays for, clearing the
operator's limit overrides.

**Deleting an account** (`POST /api/auth/account/delete`) cancels, with the provider, every subscription
still billing of the tenants the account is the only member of, before anything is deleted, since those
tenants go with it. If the provider cannot be reached, or payments are off while such a subscription
exists, nothing is deleted: 503 `BILLING_SUBSCRIPTION_NOT_CANCELLED`, and the caller tries again.

**Operations.**
- `Billing:Dodo:Environment` must be `test_mode` or `live_mode`; anything else stops the host at start.
  Once started, `PaymentGatewayCheck` asks the provider whether it accepts the API key and logs a
  Critical line if not (usually a key for the other mode).
- Webhook outcomes are counted (`wbskt_auth_billing_webhooks_total{type,outcome}`: `done`, `set_aside`,
  `failed`). `BillingWebhooksSetAside` fires on any webhook set aside in an hour (a payment that may have
  reached no tenant), `BillingWebhooksFailing` on more than five failures in an hour.
- The webhook endpoint has its own per-IP limit (`billing-webhooks`, 600 a minute), generous for a
  provider's retry burst; a refused delivery is redelivered.

Lock order, where a procedure takes more than one: the trigger key lock, then the test-run lock, then the
plan lock, then the workspace's cap lock, then the root run's counter row (`dbo.Run_Start`); the run
counters, then `dbo.WorkspaceCreditUsage`, then the tenant's grants and `dbo.TenantCreditMonths`
(`dbo.RunCredits_Post`, `dbo.Run_DeleteRetired`); the clients' and policies' rows, then the plan lock (client procedures).
