-- Redeems a confirm token: switches the account to the new address, ends every session, and issues
-- the 7-day revert token that the old address is mailed. One transaction, so a session cannot
-- outlive the switch and the undo always exists once the switch does.
--
-- 50020: the token is unknown, spent or expired, or the address has moved since it was issued.
-- 50021: the new address belongs to another account by now. UQ_Users_Email is the backstop.
CREATE PROCEDURE dbo.EmailChange_Confirm
    @TokenHash       VARBINARY(32),
    @RevertTokenHash VARBINARY(32),
    @RevertExpiresAt DATETIME2(3),
    @RevokedByIp     NVARCHAR(50) = NULL,
    @UserId          INT OUTPUT,
    @OldEmail        NVARCHAR(100) OUTPUT,
    @NewEmail        NVARCHAR(100) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;

    DECLARE @TokenId INT;

    SELECT @TokenId = T.Id,
           @UserId = T.UserId,
           @OldEmail = T.OldEmail,
           @NewEmail = T.NewEmail
    FROM dbo.EmailChangeTokens T WITH (UPDLOCK, HOLDLOCK)
    INNER JOIN dbo.Users U WITH (UPDLOCK) ON U.Id = T.UserId
    WHERE T.TokenHash = @TokenHash
      AND T.Kind = 1
      AND T.ConsumedAt IS NULL
      AND T.ExpiresAt > SYSUTCDATETIME()
      AND U.Email = T.OldEmail
      AND U.IsActive = 1;

    IF @TokenId IS NULL
    BEGIN
        ROLLBACK TRANSACTION;
        THROW 50020, 'Email change token is not valid.', 1;
    END

    IF EXISTS (SELECT 1 FROM dbo.Users WITH (UPDLOCK, HOLDLOCK) WHERE Email = @NewEmail AND Id <> @UserId)
    BEGIN
        ROLLBACK TRANSACTION;
        THROW 50021, 'That address cannot be used.', 1;
    END

    -- Pending changes only. An undo from an earlier change stays live: that is how the owner gets back
    -- when a stolen session changes the address twice.
    UPDATE dbo.EmailChangeTokens
    SET ConsumedAt = SYSUTCDATETIME()
    WHERE UserId = @UserId
      AND Kind = 1
      AND ConsumedAt IS NULL;

    UPDATE dbo.Users
    SET Email = @NewEmail,
        IsEmailVerified = 1,
        UpdatedAt = SYSUTCDATETIME()
    WHERE Id = @UserId;

    UPDATE dbo.RefreshTokens
    SET Revoked = SYSUTCDATETIME(),
        RevokedByIp = @RevokedByIp
    WHERE UserId = @UserId
      AND Revoked IS NULL;

    INSERT INTO dbo.EmailChangeTokens (UserId, Kind, TokenHash, OldEmail, NewEmail, ExpiresAt, RequestedByIp)
    VALUES (@UserId, 2, @RevertTokenHash, @OldEmail, @NewEmail, @RevertExpiresAt, @RevokedByIp);

    COMMIT TRANSACTION;
END
GO
