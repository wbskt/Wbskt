CREATE PROCEDURE dbo.Workspace_Create
    @Name NVARCHAR(100),
    @Description NVARCHAR(500),
    @OwnerUserId INT,
    @RefId UNIQUEIDENTIFIER OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    SET @RefId = NEWID();

    INSERT INTO dbo.Workspaces (RefId, Name, Description, OwnerUserId)
    VALUES (@RefId, @Name, @Description, @OwnerUserId);

    DECLARE @WorkspaceId INT = SCOPE_IDENTITY();

    INSERT INTO dbo.WorkspaceMembers (WorkspaceId, UserId, Role)
    VALUES (@WorkspaceId, @OwnerUserId, 2); -- 2: Admin
END
GO
