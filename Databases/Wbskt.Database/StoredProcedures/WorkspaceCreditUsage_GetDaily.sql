-- The workflow credits @WorkspaceId's ledger holds for each UTC day from @From to @To, both included.
-- Days with nothing posted have no row.
CREATE PROCEDURE dbo.WorkspaceCreditUsage_GetDaily
    @WorkspaceId INT,
    @From        DATE,
    @To          DATE
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        Day,
        Credits
    FROM dbo.WorkspaceCreditUsage
    WHERE WorkspaceId = @WorkspaceId
      AND Day >= @From
      AND Day <= @To
    ORDER BY Day;
END;
GO
