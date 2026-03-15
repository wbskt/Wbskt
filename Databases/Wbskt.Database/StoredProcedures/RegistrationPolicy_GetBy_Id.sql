CREATE PROCEDURE dbo.RegistrationPolicy_GetBy_Id
    @Id INT
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
    WHERE Id = @Id;
END
GO
