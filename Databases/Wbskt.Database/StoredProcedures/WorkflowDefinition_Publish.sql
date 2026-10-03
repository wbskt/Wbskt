CREATE PROCEDURE dbo.WorkflowDefinition_Publish
    @RefId          UNIQUEIDENTIFIER,
    @WorkspaceId    INT,
    @Name           NVARCHAR(200),
    @Description    NVARCHAR(2000),
    @IsEnabled      BIT,
    @DefinitionJson NVARCHAR(MAX),
    @PublishedBy    INT
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
