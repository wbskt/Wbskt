CREATE PROCEDURE [dbo].[WorkflowStepExecutions_Update]
    @Id INT,
    @Status VARCHAR(20),
    @CompletedAt DATETIME2,
    @OutputContext NVARCHAR(MAX),
    @ErrorLog NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE [dbo].[WorkflowStepExecutions]
    SET [Status] = @Status,
        [CompletedAt] = @CompletedAt,
        [OutputContext] = @OutputContext,
        [ErrorLog] = @ErrorLog
    WHERE [Id] = @Id;
END
