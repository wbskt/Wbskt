/*
    Procedure: dbo.UserRefreshTokens_Insert
    Purpose: Inserts a new refresh token.
    Parameters:
        - @UserId INT: The user Id
        - @Token VARCHAR(256): The refresh token
        - @Expires DATETIME: The expiration date of the token
        - @CreatedByIp VARCHAR(50): The IP address of the user who created the token
    Returns: None
    Author: Richard Joy
    Date: 2025-10-12
    Last Modified: 2025-10-12 by Richard Joy - Initial version
*/
CREATE PROCEDURE dbo.UserRefreshTokens_Insert
    @UserId INT,
    @Token VARCHAR(256),
    @Expires DATETIME,
    @CreatedByIp VARCHAR(50)
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.UserRefreshTokens (
        UserId,
        Token,
        Expires,
        CreatedByIp
    )
    VALUES (
        @UserId,
        @Token,
        @Expires,
        @CreatedByIp
    );
END;
