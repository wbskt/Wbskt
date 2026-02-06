CREATE PROCEDURE dbo.RegistrationPolicy_GetAll
AS
BEGIN
    SET NOCOUNT ON;

    SELECT 
        Id,
        RefId,
        Pin,
        Name,
        MaxClients,
        AutoApproval,
        CreatedAt
    FROM dbo.RegistrationPolicies
    ORDER BY CreatedAt DESC;
END
