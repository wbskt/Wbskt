CREATE PROCEDURE dbo.Workspace_GetBy_Id
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT W.Id, W.RefId, W.Name, W.Description, W.OwnerUserId, W.CreatedAt, T.RefId AS TenantRefId
    FROM dbo.Workspaces W
    INNER JOIN dbo.Tenants T ON T.Id = W.TenantId
    WHERE W.Id = @Id;
END
GO
