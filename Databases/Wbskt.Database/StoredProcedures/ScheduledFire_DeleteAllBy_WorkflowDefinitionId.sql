CREATE PROCEDURE dbo.ScheduledFire_DeleteAllBy_WorkflowDefinitionId
    @WorkflowDefinitionId INT
AS
BEGIN
    SET NOCOUNT ON;

    DELETE FROM dbo.ScheduledFires
    WHERE WorkflowDefinitionId = @WorkflowDefinitionId;
END;
GO
