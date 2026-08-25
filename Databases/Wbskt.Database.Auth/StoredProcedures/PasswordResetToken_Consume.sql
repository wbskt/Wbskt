-- Redeems a reset token: writes the new password and revokes every refresh token the account has.
--
-- All three in one transaction, deliberately. An attacker who already holds a session is the reason
-- the victim is resetting; if the revocation were a second call from the service, a crash between
-- the two would leave the new password in place and the attacker's session alive -- the one outcome
-- this endpoint exists to prevent. Duplicating four lines of RefreshToken_RevokeAllForUser is the
-- cheaper half of that trade.
--
-- An unknown token, a spent one and an expired one all raise the same error: they are the same
-- answer to the caller, and distinguishing them would say whether a token ever existed.
CREATE PROCEDURE dbo.PasswordResetToken_Consume
    @TokenHash VARBINARY(32),
    @PasswordHash NVARCHAR(255),
    @RevokedByIp NVARCHAR(50) = NULL,
    @UserId INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;

    DECLARE @TokenId INT;

    -- UPDLOCK/HOLDLOCK: two concurrent redemptions of the same token must not both pass the
    -- ConsumedAt IS NULL test. The row is held for the transaction, so the second reads the consumed
    -- row and fails the guard below.
    SELECT @TokenId = T.Id,
           @UserId = T.UserId
    FROM dbo.PasswordResetTokens T WITH (UPDLOCK, HOLDLOCK)
    WHERE T.TokenHash = @TokenHash
      AND T.ConsumedAt IS NULL
      AND T.ExpiresAt > SYSUTCDATETIME();

    IF @TokenId IS NULL
    BEGIN
        ROLLBACK TRANSACTION;
        THROW 50014, 'Password reset token is not valid.', 1;
    END

    UPDATE dbo.PasswordResetTokens
    SET ConsumedAt = SYSUTCDATETIME()
    WHERE Id = @TokenId;

    UPDATE dbo.Users
    SET PasswordHash = @PasswordHash
    WHERE Id = @UserId;

    UPDATE dbo.RefreshTokens
    SET Revoked = SYSUTCDATETIME(),
        RevokedByIp = @RevokedByIp
    WHERE UserId = @UserId
      AND Revoked IS NULL;

    COMMIT TRANSACTION;
END
GO
