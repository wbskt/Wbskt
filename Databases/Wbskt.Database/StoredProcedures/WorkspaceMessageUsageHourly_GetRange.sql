-- The client messages @WorkspaceId carried in each UTC hour from @From (included) to @To (excluded).
-- Hours with none have no row.
CREATE PROCEDURE dbo.WorkspaceMessageUsageHourly_GetRange
    @WorkspaceId INT,
    @From        DATETIME2(0),
    @To          DATETIME2(0)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        [Hour],
        Inbound,
        Outbound
    FROM dbo.WorkspaceMessageUsageHourly
    WHERE WorkspaceId = @WorkspaceId
      AND [Hour] >= @From
      AND [Hour] < @To
    ORDER BY [Hour];
END;
GO
