/*
    Procedure: dbo.Users_GetBy_EmailId
    Purpose: Retrieves a user by their email address.
    Parameters:
        - @EmailId VARCHAR(100): The email address to search for
    Returns: Id, Name, EmailId, PasswordHash, PasswordSalt
    Author: Richard Joy
    Date: 2025-04-25
    Last Modified: 2025-04-25 by Richard Joy - Initial version
*/
CREATE PROCEDURE dbo.Users_GetBy_EmailId
    @EmailId VARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        Id,
        Name,
        EmailId,
        PasswordHash,
        PasswordSalt
    FROM dbo.Users
    WHERE EmailId = @EmailId;
END;
