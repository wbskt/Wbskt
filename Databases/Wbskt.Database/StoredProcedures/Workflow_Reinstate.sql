-- Brings a deprecated workflow back as Disabled: editable again, but nothing starts until a version
-- is enabled. Refuses with 50040 when the workflow is unknown, another workspace's or deleted, and
-- with 50042 when it is not deprecated.
CREATE PROCEDURE dbo.Workflow_Reinstate
    @RefId       UNIQUEIDENTIFIER,
    @WorkspaceId INT,
    @ChangedBy   INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;

    DECLARE @State TINYINT;
    SELECT @State = State
      FROM dbo.Workflows WITH (UPDLOCK, HOLDLOCK)
     WHERE RefId = @RefId
       AND WorkspaceId = @WorkspaceId;

    IF @State IS NULL OR @State = 4
        THROW 50040, 'The workflow was not found.', 1;

    IF @State <> 3
        THROW 50042, 'The workflow is not deprecated.', 1;

    UPDATE dbo.Workflows
       SET State = 2,
           StateChangedBy = @ChangedBy,
           StateChangedAt = SYSUTCDATETIME()
     WHERE RefId = @RefId;

    COMMIT TRANSACTION;
END;
GO
