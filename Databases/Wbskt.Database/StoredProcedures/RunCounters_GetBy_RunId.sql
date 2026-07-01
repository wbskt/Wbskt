CREATE PROCEDURE dbo.RunCounters_GetBy_RunId
    @RunId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        RunId,
        ActiveBranchCount,
        CreditsConsumed,
        UpdatedAt
    FROM dbo.RunCounters
    WHERE RunId = @RunId;
END;
GO
