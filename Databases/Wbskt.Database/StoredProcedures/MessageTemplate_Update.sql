CREATE PROCEDURE dbo.MessageTemplate_Update
    @WorkspaceId INT,
    @Id INT,
    @PolicyId INT = NULL,
    @Name NVARCHAR(100),
    @MessageType NVARCHAR(100),
    @PayloadJson NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.MessageTemplates
    SET
        PolicyId = @PolicyId,
        Name = @Name,
        MessageType = @MessageType,
        PayloadJson = @PayloadJson,
        UpdatedAt = SYSUTCDATETIME()
    WHERE Id = @Id AND WorkspaceId = @WorkspaceId;
END
GO
