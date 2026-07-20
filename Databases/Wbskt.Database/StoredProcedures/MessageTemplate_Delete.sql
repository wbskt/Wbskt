CREATE PROCEDURE dbo.MessageTemplate_Delete
    @WorkspaceId INT,
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;

    DELETE FROM dbo.MessageTemplates
    WHERE Id = @Id AND WorkspaceId = @WorkspaceId;
END
GO
