-- Answers "can this user see this workspace, and what may they do there" in one call: the question
-- the management host asks on nearly every request. It replaces three round trips
-- (Workspace_FindBy_RefId, WorkspaceMember_Verify, Permission_EffectiveSet) and calls the last two
-- itself, so membership and permission rules still live in one place each.
--
-- Returns no rows when the workspace does not exist. Otherwise one row per permission slug, every row
-- carrying WorkspaceId and IsMember. A workspace the user may enter but holds no permissions in comes
-- back as a single row with a NULL Slug, and so does a workspace they are not a member of (IsMember 0),
-- for which no permissions are computed at all. IsFrozen says the workspace's tenant is frozen
-- (dbo.Tenants.FrozenAt), so its members may read but change nothing; it comes last, so a reader that
-- goes by position still finds Slug third.
CREATE PROCEDURE dbo.Workspace_ResolveAccess
    @UserId INT,
    @WorkspaceRef UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @WorkspaceId INT = (SELECT Id FROM dbo.Workspaces WHERE RefId = @WorkspaceRef);
    IF @WorkspaceId IS NULL
    BEGIN
        SELECT CAST(NULL AS INT) AS WorkspaceId, CAST(NULL AS BIT) AS IsMember, CAST(NULL AS NVARCHAR(100)) AS Slug, CAST(NULL AS BIT) AS IsFrozen
        WHERE 1 = 0;
        RETURN;
    END

    DECLARE @IsFrozen BIT = IIF(EXISTS (
        SELECT 1
        FROM dbo.Workspaces W
        INNER JOIN dbo.Tenants T ON T.Id = W.TenantId
        WHERE W.Id = @WorkspaceId
          AND T.FrozenAt IS NOT NULL), 1, 0);

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

    SELECT @WorkspaceId AS WorkspaceId, @IsMember AS IsMember, s.Slug, @IsFrozen AS IsFrozen
    FROM (SELECT 1 AS One) AS d
    LEFT JOIN @Slugs AS s ON 1 = 1;
END
GO
