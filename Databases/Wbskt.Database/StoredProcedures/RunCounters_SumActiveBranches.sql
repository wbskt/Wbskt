CREATE PROCEDURE dbo.RunCounters_SumActiveBranches
AS
BEGIN
    SET NOCOUNT ON;

    SELECT COALESCE(SUM(CAST(ActiveBranchCount AS BIGINT)), 0)
    FROM dbo.RunCounters;
END;
GO
