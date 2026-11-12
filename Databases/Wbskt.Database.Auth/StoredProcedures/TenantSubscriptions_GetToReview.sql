-- Tenants whose plan may have to change with no webhook to say so:
-- * a paid plan its subscription set, while that subscription is past due or on hold for longer than
--   the grace period, cancelled and past the period paid for, or ended;
-- * a paid plan whose subscription still reads active a day after the period it paid for ended. Its
--   renewal or its cancellation never reached us (the provider stops redelivering after about a day),
--   so it is read again from the provider;
-- * a plan an operator set, once its end date has passed.
-- TenantBillingSync decides; a tenant it leaves alone is listed again next time.
CREATE PROCEDURE dbo.TenantSubscriptions_GetToReview
    @Now          DATETIME2(3),
    @PastDueSince DATETIME2(3)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT DISTINCT s.TenantId
    FROM dbo.TenantSubscriptions s
    JOIN dbo.Tenants t ON t.Id = s.TenantId
    WHERE t.PlanCode <> N'Free'
      AND t.PlanSource = N'Subscription'
      AND t.PlanCode = s.PlanCode
      AND s.DuplicateAt IS NULL
      AND (   (s.Status IN (N'past_due', N'on_hold') AND s.PastDueSince <= @PastDueSince)
           OR (s.Status = N'cancelled' AND s.CurrentPeriodEnd <= @Now)
           OR  s.Status IN (N'expired', N'failed', N'paused')
           OR (s.Status = N'active' AND s.CurrentPeriodEnd < DATEADD(DAY, -1, @Now)))
    UNION
    SELECT t.Id
    FROM dbo.Tenants t
    WHERE t.PlanSource = N'Operator'
      AND t.PlanEndsAt <= @Now;
END
GO
