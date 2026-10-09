-- Every workspace a user is a member of, for the audit log: a sign-in or a change to the person's own
-- account is logged in each of them. Unlike Workspace_GetBy_UserId this keeps workspaces in tenants
-- that suspended the user, whose owners are exactly who should see that user's sign-in attempts.
CREATE PROCEDURE dbo.Workspace_GetIdsBy_UserId
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT WorkspaceId AS Id FROM dbo.WorkspaceMembers WHERE UserId = @UserId;
END
GO
