-- Subscriptions whose tenant may have to drop to Free with no webhook to say so: past due or on hold for
-- longer than the grace period, cancelled and past the period paid for, or ended, while the tenant is
-- still on a paid plan. TenantBillingSync decides; a tenant it leaves alone is listed again next time.
CREATE PROCEDURE dbo.TenantSubscriptions_GetToReview
    @Now          DATETIME2(3),
    @PastDueSince DATETIME2(3)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT s.TenantId
    FROM dbo.TenantSubscriptions s
    JOIN dbo.Tenants t ON t.Id = s.TenantId
    WHERE t.PlanCode <> N'Free'
      AND t.PlanCode = s.PlanCode
      AND (   (s.Status IN (N'past_due', N'on_hold') AND s.PastDueSince <= @PastDueSince)
           OR (s.Status = N'cancelled' AND s.CurrentPeriodEnd <= @Now)
           OR  s.Status IN (N'expired', N'failed', N'paused'));
END
GO
