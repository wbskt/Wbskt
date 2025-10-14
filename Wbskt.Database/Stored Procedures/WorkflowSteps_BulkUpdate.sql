CREATE PROCEDURE [dbo].[WorkflowSteps_BulkUpdate]
    @WorkflowId INT,
    @Steps [dbo].[WorkflowStepType] READONLY
AS
BEGIN
    SET NOCOUNT ON;

    -- First, delete all existing steps for this workflow
    DELETE FROM [dbo].[WorkflowSteps] WHERE [WorkflowId] = @WorkflowId;

    -- Then, insert the new set of steps
    INSERT INTO [dbo].[WorkflowSteps] (
        [WorkflowId],
        [StepOrder],
        [Name],
        [StepType],
        [StepIdentifier],
        [StepConfiguration],
        [OnSuccessStepId],
        [OnFailureStepId]
    )
    SELECT
        @WorkflowId,
        [StepOrder],
        [Name],
        [StepType],
        [StepIdentifier],
        [StepConfiguration],
        [OnSuccessStepId],
        [OnFailureStepId]
    FROM @Steps;
END
