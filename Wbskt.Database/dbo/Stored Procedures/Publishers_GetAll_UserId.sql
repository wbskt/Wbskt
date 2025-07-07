/*
    Procedure: dbo.Publishers_GetAll_UserId
    Purpose: Retrieves all publishers for a specific user modified since the specified date/time.
    Parameters:
        - @LastModified DATETIME: Only return publishers modified on or after this timestamp
        - @UserId INT: Only return publishers for this user
    Returns: Id, Name, PublisherRef, UserId, LastModified
    Author: Richard Joy
    Date: 2025-04-25
    Last Modified: 2025-04-25 by Richard Joy - Initial version
*/
CREATE PROCEDURE dbo.Publishers_GetAll_UserId
    @LastModified DATETIME,
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        Id,
        Name,
        PublisherRef,
        UserId,
        LastModified
    FROM dbo.Publishers
    WHERE UserId = @UserId
      AND LastModified >= @LastModified;
END;
