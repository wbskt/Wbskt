CREATE PROCEDURE dbo.Client_Verify
    @RefId UNIQUEIDENTIFIER,
    @Secret NVARCHAR(255)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT 
        c.Id,
        c.RefId,
        c.WorkspaceId,
        c.PolicyId,
        p.RefId AS PolicyRefId,
        c.Name,
        '********' AS Secret,
        c.Status,
        c.IsConnected,
        c.LastActivityAt,
        c.CreatedAt
    FROM dbo.Clients c
    INNER JOIN dbo.RegistrationPolicies p ON c.PolicyId = p.Id
    WHERE c.RefId = @RefId 
      AND c.Secret = @Secret;
END
GO
