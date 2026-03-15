CREATE PROCEDURE dbo.RegistrationPolicy_GetBy_RefId
    @RefId UNIQUEIDENTIFIER
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
    WHERE RefId = @RefId;
END
GO
