-- Leases up to @Take billing emails that are due to one sender until @LeaseUntil, counting the attempt,
-- so two auth instances never send the same one. A sender that dies mid-send leaves the lease to run out
-- and the mail is taken again; the relay may then see it twice, which is better than never.
CREATE PROCEDURE dbo.BillingNoticeMails_Take
    @Now        DATETIME2(3),
    @LeaseUntil DATETIME2(3),
    @Take       INT
AS
BEGIN
    SET NOCOUNT ON;

    WITH due AS (
        SELECT TOP (@Take)
            Id,
            TenantId,
            Kind,
            ToAddress,
            Subject,
            TextBody,
            HtmlBody,
            Attempts,
            NextAttemptAt
        FROM dbo.BillingNoticeMails WITH (UPDLOCK, READPAST, ROWLOCK)
        WHERE SentAt IS NULL
          AND GaveUpAt IS NULL
          AND NextAttemptAt <= @Now
        ORDER BY NextAttemptAt, Id)
    UPDATE due
    SET Attempts = Attempts + 1,
        NextAttemptAt = @LeaseUntil
    OUTPUT
        inserted.Id,
        inserted.TenantId,
        inserted.Kind,
        inserted.ToAddress,
        inserted.Subject,
        inserted.TextBody,
        inserted.HtmlBody,
        inserted.Attempts;
END
GO
