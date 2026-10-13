CREATE PROCEDURE dbo.WorkflowDefinition_Publish
    @RefId          UNIQUEIDENTIFIER,
    @WorkspaceId    INT,
    @Name           NVARCHAR(200),
    @Description    NVARCHAR(2000),
    @IsEnabled      BIT,
    @DefinitionJson NVARCHAR(MAX),
    @PublishedBy    INT,
    -- The steps that send through an integration, by its RefId. Omitted means none.
    @IntegrationRefs dbo.IntegrationRefTableType READONLY,
    -- How many workflows the workspace may have. Only a first version counts against it: a new
    -- version of an existing workflow is never refused. NULL is no ceiling.
    @MaxWorkflows   INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRAN;

    DECLARE @NextVersion INT =
        ISNULL((SELECT MAX(Version)
                  FROM dbo.WorkflowDefinitions WITH (HOLDLOCK, UPDLOCK)
                 WHERE RefId = @RefId), 0) + 1;

    -- The service checks ownership too, but from an unlocked read: two first publishes of one RefId
    -- from different workspaces could both pass it. Under the lock above only one can.
    IF EXISTS (SELECT 1 FROM dbo.WorkflowDefinitions WHERE RefId = @RefId AND WorkspaceId <> @WorkspaceId)
    BEGIN
        ROLLBACK TRAN;
        THROW 50021, 'The workflow belongs to another workspace.', 1;
    END

    -- A deleted workflow stays deleted: publishing under its RefId would bring its triggers back.
    IF EXISTS (SELECT 1 FROM dbo.WorkflowDeletions WHERE RefId = @RefId)
    BEGIN
        ROLLBACK TRAN;
        THROW 50022, 'The workflow was deleted.', 1;
    END

    -- The workspace's workflow ceiling, for a new workflow only. Counted under an application lock per
    -- workspace, so two first publishes of different workflows cannot both take the last place.
    IF @NextVersion = 1 AND @MaxWorkflows IS NOT NULL
    BEGIN
        DECLARE @Resource NVARCHAR(255) = CONCAT(N'workflow-ceiling:', @WorkspaceId);
        EXEC sp_getapplock @Resource = @Resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction';

        IF (SELECT COUNT(DISTINCT WD.RefId)
              FROM dbo.WorkflowDefinitions WD
             WHERE WD.WorkspaceId = @WorkspaceId
               AND NOT EXISTS (SELECT 1 FROM dbo.WorkflowDeletions D WHERE D.RefId = WD.RefId)) >= @MaxWorkflows
        BEGIN
            ROLLBACK TRAN;
            THROW 50024, 'The workspace has reached its workflow limit.', 1;
        END
    END

    -- Every integration a step names must be this workspace's, and stays so until the version is in:
    -- the update lock here is the one Integration_Delete takes, so a delete either finishes first
    -- (and this refuses) or waits until the version and its refs are committed (and then refuses
    -- the delete). Taken before the insert below, so neither side holds a lock the other's reads need.
    DECLARE @Refs TABLE (NodeId UNIQUEIDENTIFIER PRIMARY KEY, NodeName NVARCHAR(200), IntegrationId INT);

    INSERT INTO @Refs (NodeId, NodeName, IntegrationId)
    SELECT R.NodeId, R.NodeName, I.Id
    FROM @IntegrationRefs R
    INNER JOIN dbo.Integrations I WITH (UPDLOCK, HOLDLOCK)
        ON I.RefId = R.IntegrationRefId AND I.WorkspaceId = @WorkspaceId;

    IF (SELECT COUNT(*) FROM @Refs) <> (SELECT COUNT(*) FROM @IntegrationRefs)
    BEGIN
        ROLLBACK TRAN;
        THROW 50023, 'A step names an integration the workspace does not have.', 1;
    END

    -- This procedure is the single authority on the version number. The stored JSON is stamped here,
    -- under the same HOLDLOCK that computed it, so the row's Version column and the DefinitionJson's
    -- "version" property can never disagree - which they could when the caller pre-computed a version
    -- from an unlocked read and baked it into the JSON before calling.
    -- Property names are camelCase to match JsonSerializerDefaults.Web output.
    SET @DefinitionJson = JSON_MODIFY(
                              JSON_MODIFY(@DefinitionJson, '$.version', @NextVersion),
                              '$.isEnabled', CAST(@IsEnabled AS BIT));

    INSERT INTO dbo.WorkflowDefinitions (RefId, Version, WorkspaceId, Name, Description, IsEnabled, DefinitionJson, PublishedBy)
    VALUES (@RefId, @NextVersion, @WorkspaceId, @Name, @Description, @IsEnabled, @DefinitionJson, @PublishedBy);

    INSERT INTO dbo.WorkflowIntegrationRefs (WorkflowDefinitionId, NodeId, NodeName, IntegrationId)
    SELECT SCOPE_IDENTITY(), NodeId, NodeName, IntegrationId
    FROM @Refs;

    COMMIT TRAN;

    SELECT
        Id,
        RefId,
        Version,
        WorkspaceId,
        Name,
        Description,
        IsEnabled,
        DefinitionJson,
        PublishedBy,
        CreatedAt
      FROM dbo.WorkflowDefinitions
     WHERE RefId = @RefId AND Version = @NextVersion;
END;
GO
