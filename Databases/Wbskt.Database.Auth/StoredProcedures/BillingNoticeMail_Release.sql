-- Hands back a billing email this sender leased but never tried, so it is due again at once and the
-- attempt BillingNoticeMails_Take counted is returned. Only while the lease taken at @LeaseUntil is
-- still this sender's: a mail another instance has taken since is left alone.
CREATE PROCEDURE dbo.BillingNoticeMail_Release
    @Id         BIGINT,
    @LeaseUntil DATETIME2(3),
    @Now        DATETIME2(3)
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.BillingNoticeMails
    SET Attempts = Attempts - 1,
        NextAttemptAt = @Now
    WHERE Id = @Id
      AND SentAt IS NULL
      AND GaveUpAt IS NULL
      AND NextAttemptAt = @LeaseUntil;
END
GO
