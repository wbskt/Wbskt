CREATE PROCEDURE [dbo].[WorkflowExecutions_GetBy_Id]
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT [Id], [WorkflowRefId], [Status], [TriggeredAt], [CompletedAt], [InitialContext], [ErrorLog]
    FROM [dbo].[WorkflowExecutions]
    WHERE [Id] = @Id;
END
