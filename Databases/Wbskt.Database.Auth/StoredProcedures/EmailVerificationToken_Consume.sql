-- Redeems a verification token and marks the account's address confirmed. Single-use, enforced the
-- same way TenantInvitation_Accept enforces it.
--
-- Verifying an already-verified account is not an error worth distinguishing here: the token is
-- consumed on first use, so a second attempt fails as an invalid token like any other spent one.
CREATE PROCEDURE dbo.EmailVerificationToken_Consume
    @TokenHash VARBINARY(32),
    @UserId INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;

    DECLARE @TokenId INT;

    SELECT @TokenId = T.Id,
           @UserId = T.UserId
    FROM dbo.EmailVerificationTokens T WITH (UPDLOCK, HOLDLOCK)
    WHERE T.TokenHash = @TokenHash
      AND T.ConsumedAt IS NULL
      AND T.ExpiresAt > SYSUTCDATETIME();

    IF @TokenId IS NULL
    BEGIN
        ROLLBACK TRANSACTION;
        THROW 50015, 'Email verification token is not valid.', 1;
    END

    UPDATE dbo.EmailVerificationTokens
    SET ConsumedAt = SYSUTCDATETIME()
    WHERE Id = @TokenId;

    UPDATE dbo.Users
    SET IsEmailVerified = 1
    WHERE Id = @UserId;

    COMMIT TRANSACTION;
END
GO
