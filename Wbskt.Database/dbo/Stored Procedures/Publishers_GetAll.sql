/*
    Procedure: dbo.Publishers_GetAll
    Purpose: Retrieves all publishers modified since the specified date/time.
    Parameters:
        - @LastModified DATETIME: Only return publishers modified on or after this timestamp
    Returns: Id, Name, UserId, LastModified, PublisherRef
    Author: Richard Joy
    Date: 2025-04-25
    Last Modified: 2025-04-25 by Richard Joy - Initial version
*/
CREATE PROCEDURE dbo.Publishers_GetAll
    @LastModified DATETIME
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        Id,
        Name,
        UserId,
        LastModified,
        PublisherRef
    FROM dbo.Publishers
    WHERE LastModified >= @LastModified;
END;
