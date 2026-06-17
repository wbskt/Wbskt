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
