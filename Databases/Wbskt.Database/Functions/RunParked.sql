-- One row when every live branch of the run is waiting on a delay, signal or call: what the stats,
-- the run lists and the run page call Waiting, for a run whose status is still Running (the caller
-- checks the status, so it can keep its own filtered-index predicate). Inline, so it expands into
-- the caller's plan; one definition so the six places that use it cannot drift apart.
CREATE FUNCTION dbo.RunParked
(
    @RunId INT
)
RETURNS TABLE
AS
RETURN
    SELECT 1 AS IsParked
    WHERE EXISTS (SELECT 1 FROM dbo.Branches b WHERE b.RunId = @RunId AND b.Status = N'Waiting')
      AND NOT EXISTS (SELECT 1 FROM dbo.Branches b WHERE b.RunId = @RunId AND b.Status = N'Active');
GO
