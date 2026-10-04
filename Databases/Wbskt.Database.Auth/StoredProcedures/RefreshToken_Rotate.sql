-- Exchanges a refresh token for its replacement in one call. Retiring the presented token and
-- inserting the new one commit together or not at all, so a failure part-way can no longer leave the
-- client holding a token that is already revoked (which its next attempt would present as a replay,
-- ending every session the user has).
--
-- Returns one row. Outcome is one of:
--   Rotated       the presented token is retired and @NewTokenHash is live; the user columns are set
--   Unknown       no such token
--   Replayed      the token was already retired before this call looked at it. Treated as theft:
--                 every live token the user holds is revoked here, in the same call
--   Expired       the token was live but past its expiry
--   SessionExpired the token was live, but its session started more than @SessionLifetimeSeconds
--                 ago. Nothing is changed; the user signs in again
--   UserInactive  the account is deactivated; nothing is changed
--   Raced         another exchange retired the token between this call's read and its write. The
--                 token was used exactly once, just not by this caller, so the family is left alone
--
-- The replacement belongs to the same session: it inherits SessionId and SessionStarted, and its
-- expiry is capped at the session's end, so the session list shows when it really stops.
--
-- The read is deliberately separate from the compare-and-swap below it. A token already retired when
-- it is read is a replay; one retired in between is a lost race. Locking the row on the read would
-- serialise concurrent exchanges and make every loser look like a replay.
CREATE PROCEDURE dbo.RefreshToken_Rotate
    @TokenHash VARBINARY(32),
    @NewTokenHash VARBINARY(32),
    @NewExpires DATETIME2(3),
    @SessionLifetimeSeconds INT,
    @Ip NVARCHAR(50) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Now DATETIME2(3) = SYSUTCDATETIME();
    DECLARE @Id INT, @UserId INT, @Expires DATETIME2(3), @Revoked DATETIME2(3);
    DECLARE @SessionId UNIQUEIDENTIFIER, @SessionStarted DATETIME2(3), @SessionEnds DATETIME2(3);
    DECLARE @Outcome VARCHAR(20);

    SELECT @Id = Id, @UserId = UserId, @Expires = Expires, @Revoked = Revoked,
           @SessionId = SessionId, @SessionStarted = SessionStarted
    FROM dbo.RefreshTokens
    WHERE TokenHash = @TokenHash;

    IF @Id IS NULL
    BEGIN
        SET @Outcome = 'Unknown';
    END
    ELSE IF @Revoked IS NOT NULL
    BEGIN
        UPDATE dbo.RefreshTokens
        SET Revoked = @Now,
            RevokedByIp = @Ip
        WHERE UserId = @UserId
          AND Revoked IS NULL;

        SET @Outcome = 'Replayed';
    END
    ELSE IF @Expires <= @Now
    BEGIN
        SET @Outcome = 'Expired';
    END
    ELSE IF DATEADD(SECOND, @SessionLifetimeSeconds, @SessionStarted) <= @Now
    BEGIN
        SET @Outcome = 'SessionExpired';
    END
    ELSE IF NOT EXISTS (SELECT 1 FROM dbo.Users WHERE Id = @UserId AND IsActive = 1)
    BEGIN
        SET @Outcome = 'UserInactive';
    END
    ELSE
    BEGIN
        BEGIN TRANSACTION;

        UPDATE dbo.RefreshTokens
        SET Revoked = @Now,
            RevokedByIp = @Ip,
            ReplacedByTokenHash = @NewTokenHash
        WHERE Id = @Id
          AND Revoked IS NULL;

        IF @@ROWCOUNT = 0
        BEGIN
            ROLLBACK TRANSACTION;
            SET @Outcome = 'Raced';
        END
        ELSE
        BEGIN
            SET @SessionEnds = DATEADD(SECOND, @SessionLifetimeSeconds, @SessionStarted);

            INSERT INTO dbo.RefreshTokens (UserId, TokenHash, Expires, CreatedByIp, SessionId, SessionStarted)
            VALUES (
                @UserId,
                @NewTokenHash,
                CASE WHEN @NewExpires < @SessionEnds THEN @NewExpires ELSE @SessionEnds END,
                @Ip,
                @SessionId,
                @SessionStarted);

            COMMIT TRANSACTION;
            SET @Outcome = 'Rotated';
        END
    END

    SELECT
        @Outcome AS Outcome,
        @UserId AS UserId,
        @SessionId AS SessionId,
        u.RefId,
        u.Username,
        u.Email
    FROM (SELECT 1 AS One) AS d
    LEFT JOIN dbo.Users u ON u.Id = @UserId AND @Outcome = 'Rotated';
END
GO
