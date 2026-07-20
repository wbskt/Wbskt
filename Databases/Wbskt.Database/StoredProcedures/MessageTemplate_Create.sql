CREATE PROCEDURE dbo.MessageTemplate_Create
    @WorkspaceId INT,
    @PolicyId INT = NULL,
    @Name NVARCHAR(100),
    @MessageType NVARCHAR(100),
    @PayloadJson NVARCHAR(MAX),
    @Id INT OUTPUT,
    @RefId UNIQUEIDENTIFIER OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.MessageTemplates (WorkspaceId, PolicyId, Name, MessageType, PayloadJson)
    VALUES (@WorkspaceId, @PolicyId, @Name, @MessageType, @PayloadJson);

    SET @Id = SCOPE_IDENTITY();
    SELECT @RefId = RefId FROM dbo.MessageTemplates WHERE Id = @Id;
END
GO
