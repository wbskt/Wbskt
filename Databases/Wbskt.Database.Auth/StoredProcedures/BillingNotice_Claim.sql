-- Claims a billing notice for sending: 1 the first time a tenant's kind and period is asked for, 0
-- after. Two callers at once cannot both get 1: the key is checked and written under one lock.
CREATE PROCEDURE dbo.BillingNotice_Claim
    @TenantId  INT,
    @Kind      NVARCHAR(40),
    @PeriodKey NVARCHAR(200)
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.BillingNotices (TenantId, Kind, PeriodKey)
    SELECT @TenantId, @Kind, @PeriodKey
    WHERE EXISTS (SELECT 1 FROM dbo.Tenants WHERE Id = @TenantId)
      AND NOT EXISTS (SELECT 1
                      FROM dbo.BillingNotices WITH (UPDLOCK, HOLDLOCK)
                      WHERE TenantId = @TenantId AND Kind = @Kind AND PeriodKey = @PeriodKey);

    SELECT @@ROWCOUNT;
END
GO
