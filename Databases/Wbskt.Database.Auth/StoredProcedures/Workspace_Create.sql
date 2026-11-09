-- @WorkspaceLimit is the tenant's plan's limit on workspaces (NULL is none). The tenant's row is
-- locked before the count, so concurrent creations one short of the limit take turns.
CREATE PROCEDURE dbo.Workspace_Create
    @Name NVARCHAR(100),
    @Description NVARCHAR(500),
    @OwnerUserId INT,
    @TenantId INT,
    @RefId UNIQUEIDENTIFIER OUTPUT,
    @WorkspaceLimit INT = NULL
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

    DECLARE @WorkspaceId INT = SCOPE_IDENTITY();

    INSERT INTO dbo.WorkspaceMembers (WorkspaceId, UserId)
    VALUES (@WorkspaceId, @OwnerUserId);

    -- Owner gets the tenant's Admin role scoped to this workspace
    DECLARE @AdminRoleId INT = (SELECT Id FROM dbo.Roles WHERE TenantId = @TenantId AND Kind = 'Admin');
    IF @AdminRoleId IS NOT NULL
    BEGIN
        INSERT INTO dbo.UserRoles (UserId, RoleId, TenantId, WorkspaceId)
        VALUES (@OwnerUserId, @AdminRoleId, @TenantId, @WorkspaceId);
    END

    COMMIT TRANSACTION;
END
GO
