/*
    Procedure: dbo.Servers_UpdateStatus
    Purpose: Updates the active status of a server by Id.
    Parameters:
        - @Id INT: Server Id
        - @Active BIT: New active status
    Returns: None (updates Active field)
    Author: Richard Joy
    Date: 2024-08-26
    Last Modified: 2024-08-26 by Richard Joy - Initial version
*/
CREATE PROCEDURE dbo.Servers_UpdateStatus
    @Id INT,
    @Active BIT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.Servers
    SET Active = @Active
    WHERE Id = @Id;
END;