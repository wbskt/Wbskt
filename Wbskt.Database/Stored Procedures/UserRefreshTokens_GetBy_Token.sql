/*
    Procedure: dbo.UserRefreshTokens_GetBy_Token
    Purpose: Retrieves a refresh token by its token.
    Parameters:
        - @Token VARCHAR(256): The refresh token to search for
    Returns: Id, UserId, Token, Expires, Created, CreatedByIp, Revoked, RevokedByIp, ReplacedByToken
    Author: Richard Joy
    Date: 2025-10-12
    Last Modified: 2025-10-12 by Richard Joy - Initial version
*/
CREATE PROCEDURE dbo.UserRefreshTokens_GetBy_Token
    @Token VARCHAR(256)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        Id,
        UserId,
        Token,
        Expires,
        Created,
        CreatedByIp,
        Revoked,
        RevokedByIp,
        ReplacedByToken
    FROM dbo.UserRefreshTokens
    WHERE Token = @Token;
END;
