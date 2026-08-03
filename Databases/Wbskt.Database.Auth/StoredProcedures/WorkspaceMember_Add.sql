-- Adds an existing tenant member to a workspace.
--
-- Tenant membership is a prerequisite, not something this procedure grants. It used to insert the
-- missing TenantMembers row itself, which turned "add a member to my workspace" into "pull any
-- account in the system into my tenant" for anyone holding users.manage in a single workspace --
-- the target's email was the only thing needed, and they never consented. Joining a tenant now
-- happens exactly one way: through dbo.TenantInvitation_Accept.
--
-- The original comment was right that a workspace member without the tenant row resolves no roles
-- (every assignment is filtered by TenantId) and is invisible to Tenant_GetForUser. That state is
-- still prevented, just by rejecting the add rather than by widening the tenant.
CREATE PROCEDURE dbo.WorkspaceMember_Add
    @WorkspaceId INT,
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @TenantId INT = (SELECT TenantId FROM dbo.Workspaces WHERE Id = @WorkspaceId);

    IF @TenantId IS NULL
    BEGIN
        THROW 50004, 'Workspace does not exist.', 1;
    END

    IF NOT EXISTS (SELECT 1 FROM dbo.TenantMembers WHERE TenantId = @TenantId AND UserId = @UserId)
    BEGIN
        THROW 50009, 'User is not a member of the tenant that owns this workspace.', 1;
    END

    IF NOT EXISTS (SELECT 1 FROM dbo.WorkspaceMembers WHERE WorkspaceId = @WorkspaceId AND UserId = @UserId)
    BEGIN
        INSERT INTO dbo.WorkspaceMembers (WorkspaceId, UserId)
        VALUES (@WorkspaceId, @UserId);
    END
END
GO
