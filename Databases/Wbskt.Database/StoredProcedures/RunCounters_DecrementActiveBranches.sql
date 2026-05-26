CREATE PROCEDURE dbo.RunCounters_DecrementActiveBranches
    @RunId INT,
    @Delta INT = 1
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.RunCounters
       SET ActiveBranchCount = ActiveBranchCount - @Delta,
           UpdatedAt         = SYSUTCDATETIME()
    OUTPUT inserted.ActiveBranchCount
     WHERE RunId = @RunId;
END;
GO
