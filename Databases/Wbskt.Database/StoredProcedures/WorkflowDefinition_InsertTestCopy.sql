-- Freezes what a test run runs: a draft, or the live version, copied as a Kind = 'Test' row. It is
-- loaded by Id like any version but registers no triggers and is invisible to every version lookup.
-- @BaseVersion is the version it came from (0 for a workflow never published), which the run records
-- as its WorkflowVersion. Returns the row.
CREATE PROCEDURE dbo.WorkflowDefinition_InsertTestCopy
    @RefId          UNIQUEIDENTIFIER,
    @WorkspaceId    INT,
    @BaseVersion    INT,
    @Name           NVARCHAR(200),
    @Description    NVARCHAR(2000),
    @DefinitionJson NVARCHAR(MAX),
    @PublishedBy    INT
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (SELECT 1 FROM dbo.WorkflowDefinitions WHERE RefId = @RefId AND WorkspaceId <> @WorkspaceId)
       OR EXISTS (SELECT 1 FROM dbo.WorkflowDrafts WHERE RefId = @RefId AND WorkspaceId <> @WorkspaceId)
        THROW 50021, 'The workflow belongs to another workspace.', 1;

    IF EXISTS (SELECT 1 FROM dbo.WorkflowDeletions WHERE RefId = @RefId)
        THROW 50022, 'The workflow was deleted.', 1;

    SET @DefinitionJson = JSON_MODIFY(JSON_MODIFY(@DefinitionJson, '$.version', @BaseVersion), '$.isEnabled', CAST(1 AS BIT));

    INSERT INTO dbo.WorkflowDefinitions (RefId, Version, WorkspaceId, Name, Description, IsEnabled, DefinitionJson, PublishedBy, Kind)
    VALUES (@RefId, @BaseVersion, @WorkspaceId, @Name, @Description, 1, @DefinitionJson, @PublishedBy, N'Test');

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
     WHERE Id = SCOPE_IDENTITY();
END;
GO
