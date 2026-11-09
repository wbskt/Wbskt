-- Removes billing emails sent or given up before @Before. Their notices' claims stay, so nothing is sent again.
CREATE PROCEDURE dbo.BillingNoticeMails_Purge
    @Before DATETIME2(3)
AS
BEGIN
    SET NOCOUNT ON;

    DELETE FROM dbo.BillingNoticeMails
    WHERE (SentAt IS NOT NULL AND SentAt < @Before)
       OR (GaveUpAt IS NOT NULL AND GaveUpAt < @Before);
END
GO
