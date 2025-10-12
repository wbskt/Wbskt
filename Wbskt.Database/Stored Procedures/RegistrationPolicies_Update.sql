/*
    Procedure: dbo.RegistrationPolicies_Update
    Purpose: Updates a registration policy.
    Parameters:
        - @Id INT: The policy Id to update
        - @Name VARCHAR(100): The new name for the policy
        - @MaxClients INT: The new maximum number of clients
        - @Expiry DATETIME: The new expiration date
    Returns: None
    Author: Richard Joy
    Date: 2025-10-12
    Last Modified: 2025-10-12 by Richard Joy - Initial version
*/
CREATE PROCEDURE dbo.RegistrationPolicies_Update
    @Id INT,
    @Name VARCHAR(100),
    @MaxClients INT,
    @Expiry DATETIME
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.RegistrationPolicies
    SET
        Name = @Name,
        MaxClients = @MaxClients,
        Expiry = @Expiry
    WHERE Id = @Id;
END;
