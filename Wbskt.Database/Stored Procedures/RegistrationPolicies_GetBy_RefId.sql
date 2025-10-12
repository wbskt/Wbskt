/*
    Procedure: dbo.RegistrationPolicies_GetBy_RefId
    Purpose: Retrieves a registration policy by its unique RefId.
    Parameters:
        - @RefId UNIQUEIDENTIFIER: The policy RefId to retrieve
    Returns: Id, RefId, Name, UserId, MaxClients, Expiry, Pin, LastModified
    Author: Richard Joy
    Date: 2025-10-12
    Last Modified: 2025-10-12 by Richard Joy - Initial version
*/
CREATE PROCEDURE dbo.RegistrationPolicies_GetBy_RefId
    @RefId UNIQUEIDENTIFIER,
    @UserId INT
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
    WHERE RefId = @RefId AND UserId = @UserId;
END;
