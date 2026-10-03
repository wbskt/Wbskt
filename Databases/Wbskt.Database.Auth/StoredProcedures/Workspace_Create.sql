CREATE PROCEDURE dbo.Workspace_Create
    @Name NVARCHAR(100),
    @Description NVARCHAR(500),
    @OwnerUserId INT,
    @TenantId INT,
    @RefId UNIQUEIDENTIFIER OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @RefId = NEWID();

    BEGIN TRANSACTION;

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
