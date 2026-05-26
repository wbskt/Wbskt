CREATE PROCEDURE dbo.TriggerRegistration_DeleteAllBy_WorkflowDefinitionId
    @WorkflowDefinitionId INT
AS
BEGIN
    SET NOCOUNT ON;

    DELETE FROM dbo.TriggerRegistrations
    WHERE WorkflowDefinitionId = @WorkflowDefinitionId;
END;
GO
