/*
    Procedure: dbo.RegistrationPolicies_GetBy_Id
    Purpose: Retrieves a registration policy by its unique Id.
    Parameters:
        - @Id INT: The policy Id to retrieve
    Returns: Id, RefId, Name, UserId, MaxClients, Expiry, Pin, LastModified
    Author: Richard Joy
    Date: 2025-10-12
    Last Modified: 2025-10-12 by Richard Joy - Initial version
*/
CREATE PROCEDURE dbo.RegistrationPolicies_GetBy_Id
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        Id,
        RefId,
        Name,
        UserId,
        MaxClients,
        Expiry,
        Pin,
        LastModified
    FROM dbo.RegistrationPolicies
    WHERE Id = @Id;
END;
