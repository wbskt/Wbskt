CREATE PROCEDURE [dbo].[WorkflowStepExecutions_GetAll_ByExecutionId]
    @WorkflowExecutionId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT [Id], [WorkflowExecutionId], [WorkflowStepId], [Status], [StartedAt], [CompletedAt], [InputContext], [OutputContext], [ErrorLog]
    FROM [dbo].[WorkflowStepExecutions]
    WHERE [WorkflowExecutionId] = @WorkflowExecutionId
    ORDER BY [StartedAt];
END
