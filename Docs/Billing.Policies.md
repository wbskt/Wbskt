# Billing policies

The rules a customer is held to that are decided by people, not by the code. `Docs/Billing.md` says how
the code enforces what it does; this file says what the operators promise. The pricing page states the
customer-facing parts of it, so a change here goes there too.

## Refunds

- **Credit packs** are refundable in full within 14 days of purchase while none of the pack's credits
  have been spent. A pack's credits are spent once its grant has anything drawn (`Drawn > 0` in
  `dbo.TenantCreditGrants`). Grants are drawn soonest-to-expire first, so a newer pack is usually still
  whole while an older one is being used.
  - The operator issues the refund in the payment provider's dashboard, for the whole payment. The
    refund webhook revokes the grant; nothing else has to be done.
  - A pack that has been drawn from is not refunded, in whole or in part. A partial refund does not
    revoke the grant, so the tenant would keep the credits and the money.
  - Credits spent are never returned: they paid for runs that happened.
- **Subscriptions.** Cancelling stops the renewal and keeps the paid plan to the end of the period paid
  for. A payment can be refunded in full on request within 14 days of it being taken, monthly or
  annual; after 14 days nothing is refunded, in whole or in part.
  - The operator issues the refund in the provider's dashboard, for the whole payment, and cancels the
    subscription. The refund withholds the paid plan until the subscription is paid past the refunded
    period, so the tenant drops to Free at once.
  - A billing mistake (a wrong charge, a duplicate the code did not catch) is refunded in full whenever
    it is found.
  - A second subscription paid by mistake at the same time is cancelled and refunded automatically.
- **Chargebacks.** A pack's credits are held while a dispute on its payment is open. A lost dispute
  counts as a full refund.

## Messages over the plan

- **Free** clients are slowed to one message a minute each once the workspace has sent its monthly
  allowance, until the 1st.
- **Maker, Pro and Team** are never charged for messages past the allowance. Notices go out at 80% and 100% of
  it. At twice the allowance (`Billing:PaidMessageThrottleMultiple`, 2) the clients are held to Free's one
  message a minute until the 1st, so one account cannot load the hosts without end; a notice goes out
  when that happens. An account that needs more should move up a plan.
- **Business** is never slowed.

## Retention after a downgrade

Moving to a plan that keeps data for less time does not delete the older data at once. For 30 days
(`Billing:RetentionGraceDays`) the longest retention the workspace had keeps applying, to readings and
to run history, so moving back up within that time loses nothing. After that the new plan's retention
applies and the sweeps remove what is older.

## Credit caps

A workspace's monthly credit cap is optional and set by the tenant. Runs that would pass it are refused
or stopped; the cap is never exceeded to finish a run.
