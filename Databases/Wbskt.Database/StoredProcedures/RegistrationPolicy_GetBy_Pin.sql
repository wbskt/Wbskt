CREATE PROCEDURE dbo.RegistrationPolicy_GetBy_Pin
    @Pin NVARCHAR(20)
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
        CreatedAt,
        (SELECT COUNT(*) FROM dbo.Clients c WHERE c.PolicyId = rp.Id AND c.Status = 1) AS RegisteredClientCount,
        (SELECT COUNT(*) FROM dbo.Clients c WHERE c.PolicyId = rp.Id AND c.IsConnected = 1) AS ConnectedClientCount
    FROM dbo.RegistrationPolicies rp
    WHERE Pin = @Pin;
END
GO
