-- Ends one of a user's own sessions by id. Scoped to @UserId, so an id belonging to someone else
-- revokes nothing and reads exactly like one that does not exist.
CREATE PROCEDURE dbo.RefreshToken_RevokeForUser
    @Id          INT,
    @UserId      INT,
    @RevokedByIp NVARCHAR(50) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.RefreshTokens
    SET Revoked = SYSUTCDATETIME(),
        RevokedByIp = @RevokedByIp
    WHERE Id = @Id
      AND UserId = @UserId
      AND Revoked IS NULL
      AND Expires > SYSUTCDATETIME();

    SELECT @@ROWCOUNT AS RevokedCount;
END
GO
