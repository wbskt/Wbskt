CREATE PROCEDURE [dbo].[WorkflowExecutions_GetAll_ByWorkflowRefId]
    @WorkflowRefId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    SELECT [Id], [WorkflowRefId], [Status], [TriggeredAt], [CompletedAt], [InitialContext], [ErrorLog]
    FROM [dbo].[WorkflowExecutions]
    WHERE [WorkflowRefId] = @WorkflowRefId
    ORDER BY [TriggeredAt] DESC;
END
