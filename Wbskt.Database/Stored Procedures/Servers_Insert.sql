/*
    Procedure: dbo.Servers_Insert
    Purpose: Inserts a new server and returns the generated Id.
    Parameters:
        - @Id INT OUTPUT: Returns the new server Id
        - @IPAddress VARCHAR(100): Server IP address
        - @PublicDomainName VARCHAR(100): Public domain name
        - @Port INT: Network port
        - @Type INT: Server type
        - @Active BIT: Server status
    Returns: None (output parameter @Id is set)
    Author: Richard Joy
    Date: 2024-08-26
    Last Modified: 2024-08-26 by Richard Joy - Initial version
*/
CREATE PROCEDURE dbo.Servers_Insert
    @Id INT OUTPUT,
    @IPAddress VARCHAR(100),
    @PublicDomainName VARCHAR(100),
    @Port INT,
    @Type INT,
    @Active BIT
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.Servers (
        IPAddress,
        PublicDomainName,
        Port,
        Type,
        Active
    )
    VALUES (
        @IPAddress,
        @PublicDomainName,
        @Port,
        @Type,
        @Active
    );
    SELECT @Id = SCOPE_IDENTITY();
END;
