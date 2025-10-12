/*
    Procedure: dbo.Servers_GetAll
    Purpose: Retrieves all servers.
    Parameters:
        None
    Returns: Id, IPAddress, PublicDomainName, Port, Type, Active
    Author: Richard Joy
    Date: 2025-04-25
    Last Modified: 2025-04-25 by Richard Joy - Initial version
*/
CREATE PROCEDURE dbo.Servers_GetAll
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        Id,
        IPAddress,
        PublicDomainName,
        Port,
        Type,
        Active
    FROM dbo.Servers;
END;
