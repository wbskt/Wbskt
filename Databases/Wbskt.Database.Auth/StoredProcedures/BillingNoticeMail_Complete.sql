-- Records how one leased billing email went: sent (@SentAt), to be tried again at @RetryAt with @Error,
-- or given up (@GaveUpAt with @Error).
CREATE PROCEDURE dbo.BillingNoticeMail_Complete
    @Id       BIGINT,
    @SentAt   DATETIME2(3)   = NULL,
    @RetryAt  DATETIME2(3)   = NULL,
    @GaveUpAt DATETIME2(3)   = NULL,
    @Error    NVARCHAR(1000) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.BillingNoticeMails
    SET SentAt = @SentAt,
        GaveUpAt = @GaveUpAt,
        NextAttemptAt = ISNULL(@RetryAt, NextAttemptAt),
        LastError = @Error
    WHERE Id = @Id
      AND SentAt IS NULL;
END
GO
