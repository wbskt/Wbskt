-- Redeems a revert token: puts the account back on the address it had before the change, ends every
-- session, and drops any other pending change. Goes by the token, not by the current address, so the
-- owner's link still works when someone moved the address again after the first change.
--
-- 50020: the token is unknown, spent or expired.
-- 50021: the old address belongs to another account by now.
CREATE PROCEDURE dbo.EmailChange_Revert
    @TokenHash     VARBINARY(32),
    @RevokedByIp   NVARCHAR(50) = NULL,
    @UserId        INT OUTPUT,
    @RestoredEmail NVARCHAR(100) OUTPUT,
    @AbandonedEmail NVARCHAR(100) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;

    DECLARE @TokenId INT;

    SELECT @TokenId = T.Id,
           @UserId = T.UserId,
           @RestoredEmail = T.OldEmail
    FROM dbo.EmailChangeTokens T WITH (UPDLOCK, HOLDLOCK)
    WHERE T.TokenHash = @TokenHash
      AND T.Kind = 2
      AND T.ConsumedAt IS NULL
      AND T.ExpiresAt > SYSUTCDATETIME();

    IF @TokenId IS NULL
    BEGIN
        ROLLBACK TRANSACTION;
        THROW 50020, 'Email change token is not valid.', 1;
    END

    IF EXISTS (SELECT 1 FROM dbo.Users WITH (UPDLOCK, HOLDLOCK) WHERE Email = @RestoredEmail AND Id <> @UserId)
    BEGIN
        ROLLBACK TRANSACTION;
        THROW 50021, 'That address cannot be used.', 1;
    END

    SELECT @AbandonedEmail = Email FROM dbo.Users WITH (UPDLOCK) WHERE Id = @UserId;

    -- Every pending change goes, so one made with the stolen session cannot be confirmed afterwards,
    -- and so does this undo and every later one. Earlier undos stay: whoever holds a later address
    -- must not be able to spend the owner's way back to the address before it.
    UPDATE dbo.EmailChangeTokens
    SET ConsumedAt = SYSUTCDATETIME()
    WHERE UserId = @UserId
      AND ConsumedAt IS NULL
      AND (Kind = 1 OR Id >= @TokenId);

    UPDATE dbo.Users
    SET Email = @RestoredEmail,
        IsEmailVerified = 1,
        UpdatedAt = SYSUTCDATETIME()
    WHERE Id = @UserId;

    UPDATE dbo.RefreshTokens
    SET Revoked = SYSUTCDATETIME(),
        RevokedByIp = @RevokedByIp
    WHERE UserId = @UserId
      AND Revoked IS NULL;

    COMMIT TRANSACTION;
END
GO
