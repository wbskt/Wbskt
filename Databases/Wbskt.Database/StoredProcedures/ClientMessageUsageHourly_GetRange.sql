-- The client messages @ClientId carried in each UTC hour from @From (included) to @To (excluded), or
-- each UTC day when @Daily is 1 (a day is the sum of its hours). Buckets with none have no row.
CREATE PROCEDURE dbo.ClientMessageUsageHourly_GetRange
    @ClientId INT,
    @From     DATETIME2(0),
    @To       DATETIME2(0),
    @Daily    BIT = 0
AS
BEGIN
    SET NOCOUNT ON;

    IF @Daily = 1
    BEGIN
        SELECT
            CAST(CAST([Hour] AS DATE) AS DATETIME2(0)) AS Start,
            SUM(Inbound) AS Inbound,
            SUM(Outbound) AS Outbound
        FROM dbo.ClientMessageUsageHourly
        WHERE ClientId = @ClientId
          AND [Hour] >= @From
          AND [Hour] < @To
        GROUP BY CAST([Hour] AS DATE)
        ORDER BY Start;
        RETURN;
    END

    SELECT
        [Hour] AS Start,
        Inbound,
        Outbound
    FROM dbo.ClientMessageUsageHourly
    WHERE ClientId = @ClientId
      AND [Hour] >= @From
      AND [Hour] < @To
    ORDER BY [Hour];
END;
GO
