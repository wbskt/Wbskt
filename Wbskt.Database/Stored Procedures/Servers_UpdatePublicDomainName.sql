/*
    Procedure: dbo.Servers_UpdatePublicDomainName
    Purpose: Updates the public domain name of a server by Id.
    Parameters:
        - @Id INT: Server Id
        - @PublicDomainName VARCHAR(100): New public domain name
    Returns: None (updates PublicDomainName field)
    Author: Richard Joy
    Date: 2025-04-30
    Last Modified: 2025-04-30 by Richard Joy - Initial version
*/
CREATE PROCEDURE dbo.Servers_UpdatePublicDomainName
    @Id INT,
    @PublicDomainName VARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.Servers
    SET PublicDomainName = @PublicDomainName
    WHERE Id = @Id;
END;
