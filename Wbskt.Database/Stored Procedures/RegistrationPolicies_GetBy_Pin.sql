/*
    Procedure: dbo.RegistrationPolicies_GetBy_Pin
    Purpose: Retrieves a registration policy by its unique Pin.
    Parameters:
        - @Pin VARCHAR(6): The policy Pin to retrieve
    Returns: Id, RefId, Name, UserId, MaxClients, Expiry, Pin, LastModified
    Author: Richard Joy
    Date: 2025-10-12
    Last Modified: 2025-10-12 by Richard Joy - Initial version
*/
CREATE PROCEDURE dbo.RegistrationPolicies_GetBy_Pin
    @Pin VARCHAR(6)
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
    WHERE Pin = @Pin;
END;
