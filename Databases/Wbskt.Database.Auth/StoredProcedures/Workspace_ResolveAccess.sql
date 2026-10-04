-- Answers "can this user see this workspace, and what may they do there" in one call: the question
-- the management host asks on nearly every request. It replaces three round trips
-- (Workspace_FindBy_RefId, WorkspaceMember_Verify, Permission_EffectiveSet) and calls the last two
-- itself, so membership and permission rules still live in one place each.
--
-- Returns no rows when the workspace does not exist. Otherwise one row per permission slug, every row
-- carrying WorkspaceId and IsMember. A workspace the user may enter but holds no permissions in comes
-- back as a single row with a NULL Slug, and so does a workspace they are not a member of (IsMember 0),
-- for which no permissions are computed at all.
CREATE PROCEDURE dbo.Workspace_ResolveAccess
    @UserId INT,
    @WorkspaceRef UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @WorkspaceId INT = (SELECT Id FROM dbo.Workspaces WHERE RefId = @WorkspaceRef);
    IF @WorkspaceId IS NULL
    BEGIN
        SELECT CAST(NULL AS INT) AS WorkspaceId, CAST(NULL AS BIT) AS IsMember, CAST(NULL AS NVARCHAR(100)) AS Slug
        WHERE 1 = 0;
        RETURN;
    END

    DECLARE @Membership TABLE (IsMember BIT NOT NULL);
    INSERT INTO @Membership (IsMember)
    EXEC dbo.WorkspaceMember_Verify @UserId = @UserId, @WorkspaceId = @WorkspaceId;

    DECLARE @IsMember BIT = (SELECT TOP (1) IsMember FROM @Membership);

    DECLARE @Slugs TABLE (Slug NVARCHAR(100) NOT NULL);
    IF @IsMember = 1
    BEGIN
        INSERT INTO @Slugs (Slug)
        EXEC dbo.Permission_EffectiveSet @UserId = @UserId, @WorkspaceId = @WorkspaceId;
    END

    SELECT @WorkspaceId AS WorkspaceId, @IsMember AS IsMember, s.Slug
    FROM (SELECT 1 AS One) AS d
    LEFT JOIN @Slugs AS s ON 1 = 1;
END
GO
