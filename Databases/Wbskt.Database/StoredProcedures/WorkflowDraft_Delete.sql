-- Discards a workflow's draft. With @ExpectedRevision, only that revision is discarded, so an edit
-- saved in the meantime is kept. Returns the number of rows deleted.
CREATE PROCEDURE dbo.WorkflowDraft_Delete
    @RefId            UNIQUEIDENTIFIER,
    @WorkspaceId      INT,
    @ExpectedRevision INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DELETE FROM dbo.WorkflowDrafts
     WHERE RefId = @RefId
       AND WorkspaceId = @WorkspaceId
       AND (@ExpectedRevision IS NULL OR Revision = @ExpectedRevision);

    SELECT @@ROWCOUNT AS Deleted;
END;
GO
