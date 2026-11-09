-- Records that each of @WorkspaceIds (comma separated) is past @Percent of its account's message
-- allowance in the month starting @MonthStart, and returns those not recorded before: the ones to
-- announce. Two callers at once announce each workspace once between them.
CREATE PROCEDURE dbo.WorkspaceMessageLimit_Record
    @WorkspaceIds NVARCHAR(MAX),
    @MonthStart   DATE,
    @Percent      INT = 100
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.WorkspaceMessageLimitNotices (WorkspaceId, MonthStart, [Percent])
    OUTPUT inserted.WorkspaceId
    SELECT DISTINCT CAST(s.value AS INT), @MonthStart, @Percent
    FROM STRING_SPLIT(@WorkspaceIds, ',') s
    WHERE LTRIM(s.value) <> N''
      AND NOT EXISTS (
          SELECT 1
          FROM dbo.WorkspaceMessageLimitNotices n WITH (UPDLOCK, HOLDLOCK)
          WHERE n.WorkspaceId = CAST(s.value AS INT)
            AND n.MonthStart = @MonthStart
            AND n.[Percent] = @Percent);
END
GO
