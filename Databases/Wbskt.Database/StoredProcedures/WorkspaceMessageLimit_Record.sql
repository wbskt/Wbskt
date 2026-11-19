-- Records that each workspace in @Notices (JSON: [{ "WorkspaceId": 1, "Allowance": 1500000 }]) is past
-- @Percent of that allowance in the month starting @MonthStart, and returns those not recorded before
-- against the same allowance: the ones to announce. Two callers at once announce each workspace once
-- between them. A notice recorded with Allowance 0 matches any allowance, either way round.
CREATE PROCEDURE dbo.WorkspaceMessageLimit_Record
    @MonthStart   DATE,
    @Percent      INT = 100,
    @Notices      NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Wanted TABLE
    (
        WorkspaceId INT    NOT NULL PRIMARY KEY,
        Allowance   BIGINT NOT NULL
    );

    INSERT INTO @Wanted (WorkspaceId, Allowance)
    SELECT
        j.WorkspaceId,
        MAX(j.Allowance)
    FROM OPENJSON(@Notices)
    WITH
    (
        WorkspaceId INT    '$.WorkspaceId',
        Allowance   BIGINT '$.Allowance'
    ) j
    WHERE j.WorkspaceId IS NOT NULL
    GROUP BY j.WorkspaceId;

    INSERT INTO dbo.WorkspaceMessageLimitNotices (WorkspaceId, MonthStart, [Percent], Allowance)
    OUTPUT inserted.WorkspaceId
    SELECT
        w.WorkspaceId,
        @MonthStart,
        @Percent,
        w.Allowance
    FROM @Wanted w
    WHERE NOT EXISTS (
        SELECT 1
        FROM dbo.WorkspaceMessageLimitNotices n WITH (UPDLOCK, HOLDLOCK)
        WHERE n.WorkspaceId = w.WorkspaceId
          AND n.MonthStart = @MonthStart
          AND n.[Percent] = @Percent
          AND (n.Allowance = w.Allowance OR n.Allowance = 0 OR w.Allowance = 0));
END
GO
