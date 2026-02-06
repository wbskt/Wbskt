CREATE PROCEDURE dbo.RegistrationPolicy_GetBy_Pin
    @Pin NVARCHAR(10)
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
    WHERE Pin = @Pin;
END
