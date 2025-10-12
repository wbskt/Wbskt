/*
    Procedure: dbo.Servers_GetAll
    Purpose: Retrieves all servers.
    Parameters: None
    Returns: Id, PublicDomainName, Status
    Author: Richard Joy
    Date: 2025-10-12
    Last Modified: 2025-10-12 by Richard Joy - Initial version
*/
CREATE PROCEDURE dbo.Servers_GetAll
    @LastModified DATETIME2
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        Id,
        PublicDomainName,
        Status,
        LastModified
    FROM dbo.Servers
    WHERE LastModified > @LastModified;
END;
