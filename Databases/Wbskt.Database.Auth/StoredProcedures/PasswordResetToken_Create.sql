-- Issues a password-reset token. Supersedes any token still outstanding for the same account:
-- asking again is the normal way to replace a link that was lost or has expired, and it must not
-- fail on UX_PasswordResetTokens_Outstanding.
--
-- Takes a user id rather than an address. Deciding whether an address has an account is the caller's
-- job precisely because it must not change what the caller answers -- see AuthService.ForgotPasswordAsync.
CREATE PROCEDURE dbo.PasswordResetToken_Create
    @UserId INT,
    @TokenHash VARBINARY(32),
    @ExpiresAt DATETIME2(3),
    @RequestedByIp NVARCHAR(50) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;

    UPDATE dbo.PasswordResetTokens
    SET ConsumedAt = SYSUTCDATETIME()
    WHERE UserId = @UserId
      AND ConsumedAt IS NULL;

    INSERT INTO dbo.PasswordResetTokens (UserId, TokenHash, ExpiresAt, RequestedByIp)
    VALUES (@UserId, @TokenHash, @ExpiresAt, @RequestedByIp);

    COMMIT TRANSACTION;
END
GO
