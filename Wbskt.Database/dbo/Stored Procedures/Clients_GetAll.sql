/*
    Procedure: dbo.Clients_GetAll
    Purpose: Retrieves all clients modified since the specified date/time.
    Parameters:
        - @LastModified DATETIME: Only return clients modified on or after this timestamp
    Returns: Id, Name, UserId, ServerId, UniqueRef, LastModified
    Author: Richard Joy
    Date: 2025-04-25
    Last Modified: 2025-04-25 by Richard Joy - Initial version
*/
CREATE PROCEDURE dbo.Clients_GetAll
    @LastModified DATETIME
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        Id,
        Name,
        UserId,
        ServerId,
        UniqueRef,
        LastModified
    FROM dbo.Clients
    WHERE LastModified >= @LastModified;
END;
