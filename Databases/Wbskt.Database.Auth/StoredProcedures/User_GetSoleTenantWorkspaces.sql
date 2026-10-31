-- The workspaces that deleting this account deletes with it: every workspace of every tenant the
-- user is the only member of. Read before dbo.User_Delete so the service can write a
-- WorkspaceDeletedEvent for each; dbo.User_Delete checks the list is still the same under its locks.
CREATE PROCEDURE dbo.User_GetSoleTenantWorkspaces
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT W.Id
    FROM dbo.Workspaces W
    INNER JOIN dbo.TenantMembers TM ON TM.TenantId = W.TenantId AND TM.UserId = @UserId
    WHERE NOT EXISTS (
        SELECT 1 FROM dbo.TenantMembers O WHERE O.TenantId = W.TenantId AND O.UserId <> @UserId)
    ORDER BY W.Id;
END
GO
