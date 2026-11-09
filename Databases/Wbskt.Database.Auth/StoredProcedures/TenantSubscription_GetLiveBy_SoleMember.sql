-- The subscriptions that could still charge, of every tenant @UserId is the only member of: what deleting
-- the account must cancel with the provider first, since dbo.User_Delete deletes those tenants.
CREATE PROCEDURE dbo.TenantSubscription_GetLiveBy_SoleMember
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        s.TenantId,
        s.Provider,
        s.SubscriptionId,
        s.CustomerId,
        s.ProductId,
        s.PlanCode,
        s.BillingInterval,
        s.Status,
        s.CurrentPeriodEnd,
        s.CancelAtPeriodEnd,
        s.PastDueSince,
        s.RefundedThrough,
        s.DuplicateAt,
        s.DuplicateRefundPaymentId,
        s.CreatedAt,
        s.UpdatedAt
    FROM dbo.TenantSubscriptions s
    INNER JOIN dbo.TenantMembers TM ON TM.TenantId = s.TenantId AND TM.UserId = @UserId
    WHERE s.Status IN (N'pending', N'active', N'past_due', N'on_hold')
      AND NOT EXISTS (
          SELECT 1 FROM dbo.TenantMembers O WHERE O.TenantId = s.TenantId AND O.UserId <> @UserId);
END
GO
