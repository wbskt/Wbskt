CREATE PROCEDURE [dbo].[sp_GetRefreshToken]
    @Token NVARCHAR(255)
AS
BEGIN
    SET NOCOUNT ON;
    
    SELECT Id, UserId, Token, Expires, Revoked, ReplacedByToken
    FROM [dbo].[RefreshTokens]
    WHERE Token = @Token;
END
