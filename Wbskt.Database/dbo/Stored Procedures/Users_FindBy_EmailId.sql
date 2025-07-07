/*
    Procedure: dbo.Users_FindBy_EmailId
    Purpose: Finds a user Id by their email address.
    Parameters:
        - @EmailId VARCHAR(100): The email address to search for
    Returns: Id
    Author: Richard Joy
    Date: 2025-04-25
    Last Modified: 2025-04-25 by Richard Joy - Initial version
*/
CREATE PROCEDURE dbo.Users_FindBy_EmailId
    @EmailId VARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT Id
    FROM dbo.Users
    WHERE EmailId = @EmailId;
END;
