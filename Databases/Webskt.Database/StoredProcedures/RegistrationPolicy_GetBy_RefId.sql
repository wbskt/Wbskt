CREATE PROCEDURE dbo.RegistrationPolicy_GetBy_RefId
    @RefId UNIQUEIDENTIFIER
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
    WHERE RefId = @RefId;
END
