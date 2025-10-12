/*
    Procedure: dbo.Users_GetBy_Id
    Purpose: Retrieves a user by their unique Id.
    Parameters:
        - @Id INT: The user Id to retrieve
    Returns: Id, Name, EmailId, PasswordHash, PasswordSalt
    Author: Richard Joy
    Date: 2025-04-25
    Last Modified: 2025-04-25 by Richard Joy - Initial version
*/
CREATE PROCEDURE dbo.Users_GetBy_Id
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        Id,
        Name,
        EmailId,
        PasswordHash
    FROM dbo.Users
    WHERE Id = @Id;
END;
