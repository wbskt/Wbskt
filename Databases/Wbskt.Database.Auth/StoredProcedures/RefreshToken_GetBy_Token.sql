CREATE PROCEDURE dbo.RefreshToken_GetBy_Token
    @TokenHash VARBINARY(32)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        Id,
        UserId,
        Expires,
        Revoked
    FROM dbo.RefreshTokens
    WHERE TokenHash = @TokenHash;
END
GO
