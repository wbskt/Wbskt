-- Marks a subscription as one paid for beside an older subscription of the tenant's, before it is
-- cancelled. The first mark is kept. Returns 1 when it marked it now.
CREATE PROCEDURE dbo.TenantSubscription_MarkDuplicate
    @Provider       NVARCHAR(40),
    @SubscriptionId NVARCHAR(200),
    @Now            DATETIME2(3)
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.TenantSubscriptions
    SET DuplicateAt = @Now
    WHERE Provider = @Provider AND SubscriptionId = @SubscriptionId AND DuplicateAt IS NULL;

    SELECT @@ROWCOUNT;
END
GO
