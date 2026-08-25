-- Issues an email-verification token, superseding any still outstanding for the account. Called on
-- registration and again by resend-verification.
CREATE PROCEDURE dbo.EmailVerificationToken_Create
    @UserId INT,
    @TokenHash VARBINARY(32),
    @ExpiresAt DATETIME2(3)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;

    UPDATE dbo.EmailVerificationTokens
    SET ConsumedAt = SYSUTCDATETIME()
    WHERE UserId = @UserId
      AND ConsumedAt IS NULL;

    INSERT INTO dbo.EmailVerificationTokens (UserId, TokenHash, ExpiresAt)
    VALUES (@UserId, @TokenHash, @ExpiresAt);

    COMMIT TRANSACTION;
END
GO
