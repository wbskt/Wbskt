-- The next run number for a workflow. Called inside the caller's transaction (Run_Start, Run_Create)
-- so the row lock it takes is held until the run is inserted: concurrent starts of one workflow take
-- turns for that short tail only, and a start that rolls back hands its number back.
CREATE PROCEDURE dbo.WorkflowRunSequence_Next
    @WorkflowRefId UNIQUEIDENTIFIER,
    @Number        INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.WorkflowRunSequences
        SET @Number = LastNumber = LastNumber + 1
        WHERE WorkflowRefId = @WorkflowRefId;

    IF @@ROWCOUNT = 0
    BEGIN
        -- First run of the workflow. UPDLOCK, HOLDLOCK makes two first runs take turns on the missing
        -- key instead of both inserting; a duplicate-key error would doom the caller's transaction
        -- under XACT_ABORT, so it cannot be caught and retried instead.
        INSERT INTO dbo.WorkflowRunSequences (WorkflowRefId, LastNumber)
        SELECT @WorkflowRefId, 0
        WHERE NOT EXISTS (SELECT 1 FROM dbo.WorkflowRunSequences WITH (UPDLOCK, HOLDLOCK) WHERE WorkflowRefId = @WorkflowRefId);

        UPDATE dbo.WorkflowRunSequences
            SET @Number = LastNumber = LastNumber + 1
            WHERE WorkflowRefId = @WorkflowRefId;
    END
END;
GO
