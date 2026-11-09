-- Claims a billing notice and queues its emails in one transaction: 1 the first time a tenant's kind and
-- period is recorded, 0 after (nothing is queued then). Two callers at once cannot both get 1: the key is
-- checked and written under one lock. @Mails is a JSON array of { to, subject, htmlBody }.
CREATE PROCEDURE dbo.BillingNotice_Record
    @TenantId  INT,
    @Kind      NVARCHAR(40),
    @PeriodKey NVARCHAR(200),
    @Mails     NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;

    INSERT INTO dbo.BillingNotices (TenantId, Kind, PeriodKey)
    SELECT @TenantId, @Kind, @PeriodKey
    WHERE EXISTS (SELECT 1 FROM dbo.Tenants WHERE Id = @TenantId)
      AND NOT EXISTS (SELECT 1
                      FROM dbo.BillingNotices WITH (UPDLOCK, HOLDLOCK)
                      WHERE TenantId = @TenantId AND Kind = @Kind AND PeriodKey = @PeriodKey);

    DECLARE @Claimed INT = @@ROWCOUNT;

    IF @Claimed = 1
    BEGIN
        INSERT INTO dbo.BillingNoticeMails (TenantId, Kind, PeriodKey, ToAddress, Subject, HtmlBody)
        SELECT @TenantId, @Kind, @PeriodKey, m.[to], m.subject, m.htmlBody
        FROM OPENJSON(@Mails)
        WITH ([to] NVARCHAR(100), subject NVARCHAR(400), htmlBody NVARCHAR(MAX)) m;
    END

    COMMIT TRANSACTION;

    SELECT @Claimed;
END
GO
