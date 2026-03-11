CREATE PROCEDURE dbo.Client_GetBy_RefId
    @RefId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    SELECT 
        c.Id,
        c.RefId,
        c.PolicyId,
        p.RefId AS PolicyRefId,
        c.WorkspaceId,
        c.Name,
        c.Secret,
        c.Status,
        c.CreatedAt
    FROM dbo.Clients c
    INNER JOIN dbo.RegistrationPolicies p ON c.PolicyId = p.Id
    WHERE c.RefId = @RefId;
END
