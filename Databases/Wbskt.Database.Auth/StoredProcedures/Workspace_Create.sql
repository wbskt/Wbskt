-- @WorkspaceLimit is the tenant's plan's limit on workspaces (NULL is none). The tenant's row is
-- locked before the count, so concurrent creations one short of the limit take turns.
--
-- @OutboxMessageType and @OutboxPayload are the workspace's WorkspaceCreatedEvent, which goes into
-- dbo.OutboxMessages in this transaction, so the management host hears of the workspace (and its plan)
-- even if the host stops or the broker is down right after the commit. The caller cannot know the new
-- workspace's id and reference, so they are written into the payload's workspaceId and workspaceRefId
-- here. Both default to NULL (no outbox row) only for the previous host; required in the release after
-- (#1059).
CREATE PROCEDURE dbo.Workspace_Create
    @Name NVARCHAR(100),
    @Description NVARCHAR(500),
    @OwnerUserId INT,
    @TenantId INT,
    @RefId UNIQUEIDENTIFIER OUTPUT,
    @WorkspaceLimit INT = NULL,
    @WorkspaceId INT = NULL OUTPUT,
    @CreatedAt DATETIME2(3) = NULL OUTPUT,
    @OutboxMessageType NVARCHAR(256) = NULL,
    @OutboxPayload NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @RefId = NEWID();

    BEGIN TRANSACTION;

    IF @WorkspaceLimit IS NOT NULL
    BEGIN
        SELECT @TenantId = Id FROM dbo.Tenants WITH (UPDLOCK, ROWLOCK) WHERE Id = @TenantId;

        IF (SELECT COUNT(*) FROM dbo.Workspaces WHERE TenantId = @TenantId) >= @WorkspaceLimit
        BEGIN
            ROLLBACK TRANSACTION;
            THROW 50030, 'The plan has reached its workspace limit.', 1;
        END
    END

    INSERT INTO dbo.Workspaces (RefId, TenantId, Name, Description, OwnerUserId)
    VALUES (@RefId, @TenantId, @Name, @Description, @OwnerUserId);

    SET @WorkspaceId = SCOPE_IDENTITY();
    SET @CreatedAt = (SELECT CreatedAt FROM dbo.Workspaces WHERE Id = @WorkspaceId);
    DECLARE @WorkspaceRefId UNIQUEIDENTIFIER = @RefId;

    INSERT INTO dbo.WorkspaceMembers (WorkspaceId, UserId)
    VALUES (@WorkspaceId, @OwnerUserId);

    -- Owner gets the tenant's Admin role scoped to this workspace
    DECLARE @AdminRoleId INT = (SELECT Id FROM dbo.Roles WHERE TenantId = @TenantId AND Kind = 'Admin');
    IF @AdminRoleId IS NOT NULL
    BEGIN
        INSERT INTO dbo.UserRoles (UserId, RoleId, TenantId, WorkspaceId)
        VALUES (@OwnerUserId, @AdminRoleId, @TenantId, @WorkspaceId);
    END

    IF @OutboxMessageType IS NOT NULL
    BEGIN
        INSERT INTO dbo.OutboxMessages (MessageType, Payload)
        VALUES (@OutboxMessageType,
                JSON_MODIFY(JSON_MODIFY(@OutboxPayload, '$.workspaceId', @WorkspaceId), '$.workspaceRefId', CONVERT(NVARCHAR(36), @WorkspaceRefId)));
    END

    COMMIT TRANSACTION;
END
GO
