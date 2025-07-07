/*
    Procedure: dbo.EnrollmentPolicies_GetAll
    Purpose: Retrieves all enrollment policies modified since the specified date/time.
    Parameters:
        - @LastModified DATETIME: Only return policies modified on or after this timestamp
    Returns: Id, UserId, PolicyRef, Name, PolicyType, MaxClients, ExpiryDate, CurrentUsage, IsActive, LastModified
    Author: Richard Joy
    Date: 2025-04-25
    Last Modified: 2025-04-25 by Richard Joy - Initial version
*/
CREATE PROCEDURE dbo.EnrollmentPolicies_GetAll
    @LastModified DATETIME
AS
BEGIN
    SET NOCOUNT ON;
    
    SELECT
        Id,
        UserId,
        PolicyRef,
        Name,
        PolicyType,
        MaxClients,
        ExpiryDate,
        CurrentUsage,
        IsActive,
        LastModified
    FROM dbo.EnrollmentPolicies
    WHERE LastModified >= @LastModified;
END; 