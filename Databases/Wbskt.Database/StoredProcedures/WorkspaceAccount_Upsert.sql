-- Writes copies of workspace accounts from the auth host, a page at a time: @Accounts is a JSON array
-- of { workspaceId, tenantId, tenantRefId, planCode, limitOverrides, planRevision }.
--
-- A held copy is replaced only by one at the same or a later PlanRevision, so an event handled after
-- a newer sync, or a sync page read before a plan change and written after it, cannot put an older
-- plan back. A copy identical to the one held is left alone. Returns how many rows were inserted or
-- changed.
--
-- The update and insert run in one transaction under UPDLOCK/HOLDLOCK, so two writers inserting the
-- same new workspace take turns instead of one failing on the primary key.
CREATE PROCEDURE dbo.WorkspaceAccount_Upsert
    @Accounts NVARCHAR(MAX)
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
        PlanRevision   INT              NOT NULL
    );

    INSERT INTO @Incoming (WorkspaceId, TenantId, TenantRefId, PlanCode, LimitOverrides, PlanRevision)
    SELECT
        WorkspaceId,
        TenantId,
        TenantRefId,
        PlanCode,
        LimitOverrides,
        PlanRevision
    FROM OPENJSON(@Accounts)
    WITH
    (
        WorkspaceId    INT              '$.WorkspaceId',
        TenantId       INT              '$.TenantId',
        TenantRefId    UNIQUEIDENTIFIER '$.TenantRefId',
        PlanCode       NVARCHAR(20)     '$.PlanCode',
        LimitOverrides NVARCHAR(MAX)    '$.LimitOverrides',
        PlanRevision   INT              '$.PlanRevision'
    );

    DECLARE @Changed INT;

    BEGIN TRANSACTION;

    UPDATE wa
    SET wa.TenantId = i.TenantId,
        wa.TenantRefId = i.TenantRefId,
        wa.PlanCode = i.PlanCode,
        wa.LimitOverrides = i.LimitOverrides,
        wa.PlanRevision = i.PlanRevision,
        wa.UpdatedAt = SYSUTCDATETIME()
    FROM dbo.WorkspaceAccounts wa WITH (UPDLOCK, HOLDLOCK)
    JOIN @Incoming i ON i.WorkspaceId = wa.WorkspaceId
    WHERE i.PlanRevision >= wa.PlanRevision
      AND (wa.TenantId <> i.TenantId
           OR wa.TenantRefId <> i.TenantRefId
           OR wa.PlanCode <> i.PlanCode
           OR wa.PlanRevision <> i.PlanRevision
           OR ISNULL(wa.LimitOverrides, N'') <> ISNULL(i.LimitOverrides, N''));

    SET @Changed = @@ROWCOUNT;

    INSERT INTO dbo.WorkspaceAccounts (WorkspaceId, TenantId, TenantRefId, PlanCode, LimitOverrides, PlanRevision)
    SELECT
        i.WorkspaceId,
        i.TenantId,
        i.TenantRefId,
        i.PlanCode,
        i.LimitOverrides,
        i.PlanRevision
    FROM @Incoming i
    WHERE NOT EXISTS (SELECT 1 FROM dbo.WorkspaceAccounts wa WITH (UPDLOCK, HOLDLOCK) WHERE wa.WorkspaceId = i.WorkspaceId);

    SET @Changed += @@ROWCOUNT;

    COMMIT TRANSACTION;

    SELECT @Changed;
END
GO
