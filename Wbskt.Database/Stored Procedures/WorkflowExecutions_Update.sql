CREATE PROCEDURE [dbo].[WorkflowExecutions_Update]
    @Id INT,
    @Status VARCHAR(20),
    @CompletedAt DATETIME2,
    @ErrorLog NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE [dbo].[WorkflowExecutions]
    SET [Status] = @Status,
        [CompletedAt] = @CompletedAt,
        [ErrorLog] = @ErrorLog
    WHERE [Id] = @Id;
END
