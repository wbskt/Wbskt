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
    WHERE LastModified >= @LastModified
    ORDER BY LastModified DESC;
END; 