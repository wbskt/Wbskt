-- The tenant's workspaces still in use, from their account copies. A deleted workspace's copy
-- (RetiredAt) is left out. The management host pushes news about the account to each of them.
CREATE PROCEDURE dbo.WorkspaceAccount_GetBy_TenantId
    @TenantId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        WorkspaceId
    FROM dbo.WorkspaceAccounts
    WHERE TenantId = @TenantId
      AND RetiredAt IS NULL
    ORDER BY WorkspaceId;
END
GO
