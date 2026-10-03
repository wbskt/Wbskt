CREATE PROCEDURE dbo.Tenant_GetForUser
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT t.Id, t.RefId, t.Name, t.Description, t.CreatedAt
    FROM dbo.Tenants t
    INNER JOIN dbo.TenantMembers tm ON tm.TenantId = t.Id
    WHERE tm.UserId = @UserId
      AND tm.IsSuspended = 0;
END
GO
