-- Writes copies of workspace accounts from the auth host, a page at a time: @Accounts is a JSON array
-- of { workspaceId, tenantId, tenantRefId, planCode, limitOverrides, planRevision, creditsPerMonth,
-- rawReadingsDays, hourlyReadingsDays, runHistoryDays }.
--
-- A held copy is replaced only by one at the same or a later PlanRevision, so an event handled after
-- a newer sync, or a sync page read before a plan change and written after it, cannot put an older
-- plan back. A copy identical to the one held is left alone; one whose CreditsPerMonth changed (the
-- catalogue changed in a deploy) is written at the same revision. Returns how many rows were inserted or
-- changed.
--
-- A copy whose retention shrinks keeps what it had for @RetentionGraceDays (see dbo.WorkspaceAccounts):
-- each kind holds the longer of the days it had and any hold still running, so two downgrades in a row
-- keep the first plan's days, and the hold runs from the latest change.
--
-- The update and insert run in one transaction under UPDLOCK/HOLDLOCK, so two writers inserting the
-- same new workspace take turns instead of one failing on the primary key.
CREATE PROCEDURE dbo.WorkspaceAccount_Upsert
    @Accounts           NVARCHAR(MAX),
    @RetentionGraceDays INT = 30
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Incoming TABLE
    (
        WorkspaceId    INT              NOT NULL PRIMARY KEY,
        TenantId       INT              NOT NULL,
        TenantRefId    UNIQUEIDENTIFIER NOT NULL,
        PlanCode       NVARCHAR(20)     NOT NULL,
        LimitOverrides NVARCHAR(MAX)    NULL,
        PlanRevision   INT              NOT NULL,
        CreditsPerMonth DECIMAL(18,4)   NULL,
        RawReadingsDays    INT          NULL,
        HourlyReadingsDays INT          NULL,
        RunHistoryDays     INT          NULL
    );

    INSERT INTO @Incoming (WorkspaceId, TenantId, TenantRefId, PlanCode, LimitOverrides, PlanRevision, CreditsPerMonth, RawReadingsDays, HourlyReadingsDays, RunHistoryDays)
    SELECT
        WorkspaceId,
        TenantId,
        TenantRefId,
        PlanCode,
        LimitOverrides,
        PlanRevision,
        CreditsPerMonth,
        RawReadingsDays,
        HourlyReadingsDays,
        RunHistoryDays
    FROM OPENJSON(@Accounts)
    WITH
    (
        WorkspaceId    INT              '$.WorkspaceId',
        TenantId       INT              '$.TenantId',
        TenantRefId    UNIQUEIDENTIFIER '$.TenantRefId',
        PlanCode       NVARCHAR(20)     '$.PlanCode',
        LimitOverrides NVARCHAR(MAX)    '$.LimitOverrides',
        PlanRevision   INT              '$.PlanRevision',
        CreditsPerMonth DECIMAL(18,4)   '$.CreditsPerMonth',
        RawReadingsDays    INT          '$.RawReadingsDays',
        HourlyReadingsDays INT          '$.HourlyReadingsDays',
        RunHistoryDays     INT          '$.RunHistoryDays'
    );

    DECLARE @Changed INT;
    DECLARE @Now DATETIME2(3) = SYSUTCDATETIME();

    BEGIN TRANSACTION;

    UPDATE wa
    SET wa.TenantId = i.TenantId,
        wa.TenantRefId = i.TenantRefId,
        wa.PlanCode = i.PlanCode,
        wa.LimitOverrides = i.LimitOverrides,
        wa.PlanRevision = i.PlanRevision,
        wa.CreditsPerMonth = i.CreditsPerMonth,
        wa.RawReadingsDays = i.RawReadingsDays,
        wa.HourlyReadingsDays = i.HourlyReadingsDays,
        wa.RunHistoryDays = i.RunHistoryDays,
        wa.HeldRawReadingsDays = CASE WHEN s.Shrinks = 1 THEN CASE WHEN wa.RetentionHeldUntil > @Now AND wa.HeldRawReadingsDays > wa.RawReadingsDays THEN wa.HeldRawReadingsDays ELSE wa.RawReadingsDays END ELSE wa.HeldRawReadingsDays END,
        wa.HeldHourlyReadingsDays = CASE WHEN s.Shrinks = 1 THEN CASE WHEN wa.RetentionHeldUntil > @Now AND wa.HeldHourlyReadingsDays > wa.HourlyReadingsDays THEN wa.HeldHourlyReadingsDays ELSE wa.HourlyReadingsDays END ELSE wa.HeldHourlyReadingsDays END,
        wa.HeldRunHistoryDays = CASE WHEN s.Shrinks = 1 THEN CASE WHEN wa.RetentionHeldUntil > @Now AND wa.HeldRunHistoryDays > wa.RunHistoryDays THEN wa.HeldRunHistoryDays ELSE wa.RunHistoryDays END ELSE wa.HeldRunHistoryDays END,
        wa.RetentionHeldUntil = CASE WHEN s.Shrinks = 1 THEN DATEADD(DAY, @RetentionGraceDays, @Now) ELSE wa.RetentionHeldUntil END,
        wa.UpdatedAt = @Now
    FROM dbo.WorkspaceAccounts wa WITH (UPDLOCK, HOLDLOCK)
    JOIN @Incoming i ON i.WorkspaceId = wa.WorkspaceId
    CROSS APPLY (SELECT CASE WHEN i.RawReadingsDays < wa.RawReadingsDays
                              OR i.HourlyReadingsDays < wa.HourlyReadingsDays
                              OR i.RunHistoryDays < wa.RunHistoryDays THEN 1 ELSE 0 END AS Shrinks) s
    WHERE i.PlanRevision >= wa.PlanRevision
      AND (wa.TenantId <> i.TenantId
           OR wa.TenantRefId <> i.TenantRefId
           OR wa.PlanCode <> i.PlanCode
           OR wa.PlanRevision <> i.PlanRevision
           OR ISNULL(wa.LimitOverrides, N'') <> ISNULL(i.LimitOverrides, N'')
           OR ISNULL(wa.CreditsPerMonth, -1) <> ISNULL(i.CreditsPerMonth, -1)
           OR ISNULL(wa.RawReadingsDays, -1) <> ISNULL(i.RawReadingsDays, -1)
           OR ISNULL(wa.HourlyReadingsDays, -1) <> ISNULL(i.HourlyReadingsDays, -1)
           OR ISNULL(wa.RunHistoryDays, -1) <> ISNULL(i.RunHistoryDays, -1));

    SET @Changed = @@ROWCOUNT;

    INSERT INTO dbo.WorkspaceAccounts (WorkspaceId, TenantId, TenantRefId, PlanCode, LimitOverrides, PlanRevision, CreditsPerMonth, RawReadingsDays, HourlyReadingsDays, RunHistoryDays)
    SELECT
        i.WorkspaceId,
        i.TenantId,
        i.TenantRefId,
        i.PlanCode,
        i.LimitOverrides,
        i.PlanRevision,
        i.CreditsPerMonth,
        i.RawReadingsDays,
        i.HourlyReadingsDays,
        i.RunHistoryDays
    FROM @Incoming i
    WHERE NOT EXISTS (SELECT 1 FROM dbo.WorkspaceAccounts wa WITH (UPDLOCK, HOLDLOCK) WHERE wa.WorkspaceId = i.WorkspaceId);

    SET @Changed += @@ROWCOUNT;

    COMMIT TRANSACTION;

    SELECT @Changed;
END
GO
