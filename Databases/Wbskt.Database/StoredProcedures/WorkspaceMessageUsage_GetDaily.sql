-- The client messages @WorkspaceId carried on each UTC day from @From to @To, both included. Days with
-- none have no row.
CREATE PROCEDURE dbo.WorkspaceMessageUsage_GetDaily
    @WorkspaceId INT,
    @From        DATE,
    @To          DATE
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        Day,
        Inbound,
        Outbound
    FROM dbo.WorkspaceMessageUsage
    WHERE WorkspaceId = @WorkspaceId
      AND Day >= @From
      AND Day <= @To
    ORDER BY Day;
END;
GO
