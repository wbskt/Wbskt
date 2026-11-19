-- Refuses with 50045 when the workspace already holds @MaxPerWorkspace templates; a duplicate name
-- fails on UQ_MessageTemplates_WorkspaceId_Name (2627).
CREATE PROCEDURE dbo.MessageTemplate_Create
    @WorkspaceId INT,
    @PolicyId INT = NULL,
    @Name NVARCHAR(100),
    @MessageType NVARCHAR(100),
    @PayloadJson NVARCHAR(MAX),
    @Id INT OUTPUT,
    @RefId UNIQUEIDENTIFIER OUTPUT,
    -- NULL (no limit) only for the previous image, which does not pass it; the default goes in the
    -- release after (#1059).
    @MaxPerWorkspace INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRAN;

    -- Under the range lock two concurrent adds cannot both see room for one more.
    IF @MaxPerWorkspace IS NOT NULL
       AND (SELECT COUNT(*) FROM dbo.MessageTemplates WITH (UPDLOCK, HOLDLOCK) WHERE WorkspaceId = @WorkspaceId) >= @MaxPerWorkspace
    BEGIN
        ROLLBACK TRAN;
        THROW 50045, 'The workspace has reached its message template limit.', 1;
    END

    INSERT INTO dbo.MessageTemplates (WorkspaceId, PolicyId, Name, MessageType, PayloadJson)
    VALUES (@WorkspaceId, @PolicyId, @Name, @MessageType, @PayloadJson);

    SET @Id = SCOPE_IDENTITY();
    SELECT @RefId = RefId FROM dbo.MessageTemplates WHERE Id = @Id;

    COMMIT TRAN;
END
GO
