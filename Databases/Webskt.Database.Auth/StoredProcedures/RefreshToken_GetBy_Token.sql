CREATE PROCEDURE dbo.RefreshToken_GetBy_Token
    @Token NVARCHAR(255)
AS
BEGIN
    SET NOCOUNT ON;
    
    SELECT 
        Id, 
        UserId, 
        Token, 
        Expires, 
        Revoked, 
        ReplacedByToken
    FROM dbo.RefreshTokens
    WHERE Token = @Token;
END
