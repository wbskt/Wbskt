-- Records that each of @WorkspaceIds (comma separated) is over its account's message allowance in the
-- month starting @MonthStart, and returns those not recorded before: the ones to announce. Two callers at
-- once announce each workspace once between them.
CREATE PROCEDURE dbo.WorkspaceMessageLimit_Record
    @WorkspaceIds NVARCHAR(MAX),
    @MonthStart   DATE
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.WorkspaceMessageLimitNotices (WorkspaceId, MonthStart)
    OUTPUT inserted.WorkspaceId
    SELECT DISTINCT CAST(s.value AS INT), @MonthStart
    FROM STRING_SPLIT(@WorkspaceIds, ',') s
    WHERE LTRIM(s.value) <> N''
      AND NOT EXISTS (
          SELECT 1
          FROM dbo.WorkspaceMessageLimitNotices n WITH (UPDLOCK, HOLDLOCK)
          WHERE n.WorkspaceId = CAST(s.value AS INT)
            AND n.MonthStart = @MonthStart);
END
GO
