/*
    Procedure: dbo.UserRefreshTokens_Update
    Purpose: Updates a refresh token.
    Parameters:
        - @Id INT: The refresh token Id
        - @Revoked DATETIME: The date when the token was revoked
        - @RevokedByIp VARCHAR(50): The IP address of the user who revoked the token
        - @ReplacedByToken VARCHAR(256): The token that replaced this token
    Returns: None
    Author: Richard Joy
    Date: 2025-10-12
    Last Modified: 2025-10-12 by Richard Joy - Initial version
*/
CREATE PROCEDURE dbo.UserRefreshTokens_Update
    @Id INT,
    @Revoked DATETIME2,
    @RevokedByIp VARCHAR(50),
    @ReplacedByToken VARCHAR(256)
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.UserRefreshTokens
    SET
        Revoked = @Revoked,
        RevokedByIp = @RevokedByIp,
        ReplacedByToken = @ReplacedByToken
    WHERE Id = @Id;
END;
