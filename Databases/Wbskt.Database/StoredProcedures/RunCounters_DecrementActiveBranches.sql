CREATE PROCEDURE dbo.RunCounters_DecrementActiveBranches
    @RunId INT,
    @Delta INT = 1
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.RunCounters WITH (ROWLOCK)
       -- Clamped: a decrement replayed after a crash must not drive the count negative, where the
       -- run would never be seen as having no active branches.
       SET ActiveBranchCount = IIF(ActiveBranchCount - @Delta < 0, 0, ActiveBranchCount - @Delta),
           UpdatedAt         = SYSUTCDATETIME()
    OUTPUT inserted.ActiveBranchCount
     WHERE RunId = @RunId;
END;
GO
