CREATE PROCEDURE dbo.RegistrationPolicy_GetBy_Pin
    @Pin NVARCHAR(10)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT 
        Id,
        RefId,
        WorkspaceId,
        Pin,
        Name,
        MaxClients,
        AutoApproval,
        IsEnabled,
        CreatedAt
    FROM dbo.RegistrationPolicies
    WHERE Pin = @Pin;
END
GO
