-- Writes the tenant's subscription as just read from the provider. PastDueSince is kept from the stored
-- row while the subscription stays past due or on hold, set when it becomes so, and cleared otherwise;
-- RefundedThrough is kept unless @RefundedThrough sets it. Returns the stored row.
CREATE PROCEDURE dbo.TenantSubscription_Upsert
    @TenantId          INT,
    @Provider          NVARCHAR(40),
    @SubscriptionId    NVARCHAR(200),
    @CustomerId        NVARCHAR(200),
    @ProductId         NVARCHAR(200),
    @PlanCode          NVARCHAR(20),
    @BillingInterval   NVARCHAR(20),
    @Status            NVARCHAR(20),
    @CurrentPeriodEnd  DATETIME2(3),
    @CancelAtPeriodEnd BIT,
    @Now               DATETIME2(3),
    @RefundedThrough   DATETIME2(3) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @PastDue BIT = CASE WHEN @Status IN (N'past_due', N'on_hold') THEN 1 ELSE 0 END;

    BEGIN TRANSACTION;

    -- Another subscription of the tenant's is replaced, and so is this subscription's row under another
    -- tenant (it cannot move, but a stale row must not block the unique key).
    DELETE FROM dbo.TenantSubscriptions
    WHERE Provider = @Provider AND SubscriptionId = @SubscriptionId AND TenantId <> @TenantId;

    MERGE dbo.TenantSubscriptions WITH (HOLDLOCK) AS t
    USING (SELECT @TenantId AS TenantId) AS s
       ON t.TenantId = s.TenantId
    WHEN MATCHED THEN
        UPDATE SET
            Provider = @Provider,
            PastDueSince = CASE
                WHEN @PastDue = 0 THEN NULL
                WHEN t.SubscriptionId = @SubscriptionId AND t.PastDueSince IS NOT NULL THEN t.PastDueSince
                ELSE @Now END,
            RefundedThrough = CASE
                WHEN @RefundedThrough IS NOT NULL THEN @RefundedThrough
                WHEN t.SubscriptionId = @SubscriptionId THEN t.RefundedThrough
                ELSE NULL END,
            SubscriptionId = @SubscriptionId,
            CustomerId = @CustomerId,
            ProductId = @ProductId,
            PlanCode = @PlanCode,
            BillingInterval = @BillingInterval,
            Status = @Status,
            CurrentPeriodEnd = @CurrentPeriodEnd,
            CancelAtPeriodEnd = @CancelAtPeriodEnd,
            UpdatedAt = @Now
    WHEN NOT MATCHED THEN
        INSERT (TenantId, Provider, SubscriptionId, CustomerId, ProductId, PlanCode, BillingInterval, Status, CurrentPeriodEnd, CancelAtPeriodEnd, PastDueSince, RefundedThrough, UpdatedAt)
        VALUES (@TenantId, @Provider, @SubscriptionId, @CustomerId, @ProductId, @PlanCode, @BillingInterval, @Status, @CurrentPeriodEnd, @CancelAtPeriodEnd,
                CASE WHEN @PastDue = 1 THEN @Now END, @RefundedThrough, @Now);

    COMMIT TRANSACTION;

    EXEC dbo.TenantSubscription_GetBy_TenantId @TenantId = @TenantId;
END
GO
