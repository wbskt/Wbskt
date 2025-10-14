CREATE PROCEDURE [dbo].[WorkflowStepExecutions_Insert]
    @WorkflowExecutionId INT,
    @WorkflowStepId INT,
    @Status VARCHAR(20),
    @StartedAt DATETIME2,
    @InputContext NVARCHAR(MAX),
    @Id INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO [dbo].[WorkflowStepExecutions] ([WorkflowExecutionId], [WorkflowStepId], [Status], [StartedAt], [InputContext])
    VALUES (@WorkflowExecutionId, @WorkflowStepId, @Status, @StartedAt, @InputContext);

    SET @Id = SCOPE_IDENTITY();
END
