CREATE PROCEDURE [dbo].[WorkflowSteps_GetBy_WorkflowId]
    @WorkflowId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT [Id], [WorkflowId], [StepOrder], [Name], [StepType], [StepIdentifier], [StepConfiguration], [OnSuccessStepId], [OnFailureStepId]
    FROM [dbo].[WorkflowSteps]
    WHERE [WorkflowId] = @WorkflowId
    ORDER BY [StepOrder];
END
