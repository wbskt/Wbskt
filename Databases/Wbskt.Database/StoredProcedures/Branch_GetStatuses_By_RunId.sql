CREATE PROCEDURE dbo.Branch_GetStatuses_By_RunId
    @RunId INT
AS
BEGIN
    SET NOCOUNT ON;

    -- The statuses a run's branches ended in, each once: all the finalizer needs to pick the run's
    -- terminal status, without reading every branch's local state.
    SELECT DISTINCT
        Status
    FROM dbo.Branches
    WHERE RunId = @RunId;
END;
GO
