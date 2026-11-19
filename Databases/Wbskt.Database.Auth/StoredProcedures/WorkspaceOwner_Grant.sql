-- Makes @UserId the owner of a workspace, in the caller's transaction, as every hand-over does: the
-- explicit transfer (dbo.Workspace_SetOwner) and the workspaces a member leaves behind
-- (dbo.TenantMember_Remove, and through it leaving a tenant and deleting an account).
--
-- The new owner is added to the workspace if they were not in it, since an owner who is not a member
-- cannot reach what they own. Like a workspace's creator (dbo.Workspace_Create), they get the tenant's
-- Admin role scoped to the workspace unless they hold it there or tenant-wide, so the owner can
-- administer what they own.
--
-- With @Announce, queues a WorkspaceOwnershipTransferredEvent in dbo.OutboxMessages naming the previous
-- and new owners, for the workspace's audit log; the payload is the event's JSON as the outbox reads it.
-- An explicit transfer passes 0 and publishes the event itself, so it names who made the transfer.
CREATE PROCEDURE dbo.WorkspaceOwner_Grant
    @WorkspaceId INT,
    @UserId INT,
    @TenantId INT,
    @Announce BIT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @PreviousOwnerRefId UNIQUEIDENTIFIER = (
        SELECT U.RefId
        FROM dbo.Workspaces W
        INNER JOIN dbo.Users U ON U.Id = W.OwnerUserId
        WHERE W.Id = @WorkspaceId);
    DECLARE @NewOwnerRefId UNIQUEIDENTIFIER = (SELECT RefId FROM dbo.Users WHERE Id = @UserId);

    IF NOT EXISTS (SELECT 1 FROM dbo.WorkspaceMembers WHERE WorkspaceId = @WorkspaceId AND UserId = @UserId)
    BEGIN
        INSERT INTO dbo.WorkspaceMembers (WorkspaceId, UserId)
        VALUES (@WorkspaceId, @UserId);
    END

    UPDATE dbo.Workspaces
    SET OwnerUserId = @UserId
    WHERE Id = @WorkspaceId;

    DECLARE @AdminRoleId INT = (SELECT Id FROM dbo.Roles WHERE TenantId = @TenantId AND Kind = N'Admin');
    IF @AdminRoleId IS NOT NULL
        AND NOT EXISTS (
            SELECT 1
            FROM dbo.UserRoles
            WHERE UserId = @UserId
              AND RoleId = @AdminRoleId
              AND TenantId = @TenantId
              AND (WorkspaceId = @WorkspaceId OR WorkspaceId IS NULL))
    BEGIN
        INSERT INTO dbo.UserRoles (UserId, RoleId, TenantId, WorkspaceId)
        VALUES (@UserId, @AdminRoleId, @TenantId, @WorkspaceId);
    END

    IF @Announce = 1
    BEGIN
        INSERT INTO dbo.OutboxMessages (MessageType, Payload)
        SELECT N'Wbskt.Events.Auth.WorkspaceOwnershipTransferredEvent',
               (SELECT @WorkspaceId AS workspaceId,
                       @NewOwnerRefId AS newOwnerRefId,
                       JSON_QUERY((SELECT N'owner' AS field,
                                          LOWER(CONVERT(NVARCHAR(36), @PreviousOwnerRefId)) AS [before],
                                          LOWER(CONVERT(NVARCHAR(36), @NewOwnerRefId)) AS [after]
                                   FOR JSON PATH, INCLUDE_NULL_VALUES)) AS changes,
                       CONVERT(NVARCHAR(30), SYSUTCDATETIME(), 127) + N'Z' AS createdAtUtc
                FOR JSON PATH, WITHOUT_ARRAY_WRAPPER);
    END
END
GO
