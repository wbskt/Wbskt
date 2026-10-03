CREATE PROCEDURE dbo.JoinAggregator_DeleteAllBy_RunId
    @RunId INT
AS
BEGIN
    SET NOCOUNT ON;

    -- Called by RunFinalizer alongside the bookmark delete. Aggregator rows are kept for the life of
    -- the run (they are useful when inspecting a stuck cohort) and only collected at terminal.
    DELETE C
    FROM dbo.JoinContributions C
    INNER JOIN dbo.JoinAggregators A ON A.JoinToken = C.JoinToken
    WHERE A.RunId = @RunId;

    DELETE FROM dbo.JoinAggregators
    WHERE RunId = @RunId;
END;
GO
