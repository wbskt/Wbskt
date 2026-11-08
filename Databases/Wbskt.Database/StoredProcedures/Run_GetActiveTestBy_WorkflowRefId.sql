-- The test run of a workflow that is going, if any (at most one, per Run_Start).
CREATE PROCEDURE dbo.Run_GetActiveTestBy_WorkflowRefId
    @WorkflowRefId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    -- IX_Runs_WorkflowRefId_Test_Active's filter word for word.
    SELECT TOP (1) RefId
      FROM dbo.Runs
     WHERE WorkflowRefId = @WorkflowRefId
       AND IsTest = 1
       AND Status IN (N'Running', N'Failing', N'Cancelling');
END;
GO
