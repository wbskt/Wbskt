-- Revokes every live refresh token for a user. Used by sign-out-everywhere, by account
-- deactivation, and when a retired token is replayed (which implies the token has leaked).
CREATE PROCEDURE dbo.RefreshToken_RevokeAllForUser
    @UserId INT,
    @RevokedByIp NVARCHAR(50) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @RevokedCount INT;
    EXEC dbo.RefreshTokens_EndAllForUser @UserId = @UserId, @RevokedByIp = @RevokedByIp, @RevokedCount = @RevokedCount OUTPUT;

    SELECT @RevokedCount AS RevokedCount;
END
GO
