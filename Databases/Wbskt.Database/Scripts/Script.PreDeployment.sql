/*
--------------------------------------------------------------------------------------
Pre-Deployment Script: Snapshot Isolation Configuration
--------------------------------------------------------------------------------------
*/

-- 1. Enable Read Committed Snapshot Isolation (RCSI)
-- Improves concurrency: Readers do NOT block Writers, and Writers do NOT block Readers.
IF (SELECT is_read_committed_snapshot_on
    FROM sys.databases
    WHERE name = DB_NAME()) = 0
BEGIN
    PRINT '>>> Configuring Database: Enabling READ_COMMITTED_SNAPSHOT for ' + DB_NAME();

    DECLARE @dbName  NVARCHAR(256) = DB_NAME();
    DECLARE @stmt    NVARCHAR(MAX) = N'ALTER DATABASE [' + @dbName + N'] 
                                         SET READ_COMMITTED_SNAPSHOT ON 
                                         WITH ROLLBACK IMMEDIATE;';

    -- EXECUTE WITH elevated awareness of connection termination
    EXEC sp_executesql @stmt;

    PRINT '>>> Success: READ_COMMITTED_SNAPSHOT is now ON.';
END
ELSE
    BEGIN
    PRINT '>>> Status: READ_COMMITTED_SNAPSHOT is already enabled for ' + DB_NAME();
END
GO
/*
--------------------------------------------------------------------------------------
Pre-Deployment Script: Clients.Secret -> Clients.SecretHash
--------------------------------------------------------------------------------------
Client secrets used to be stored in plaintext and compared inside Client_Verify. They are now
stored as SHA-256 and compared in C#. The plaintext column is dropped by the schema diff that
follows this script, so the backfill has to happen HERE - once the diff runs, there is nothing
left to hash.

Because it drops a column, the deploy carrying this change needs MIGRATE_ALLOW_DATA_LOSS=true
(deploy/README.md). It is the only deploy that does.

Every statement touching either column goes through sp_executesql. Direct references would be
resolved when the batch is parsed, and fail on whichever database does not have that column yet -
a fresh one has no Secret, a migrated one has no SecretHash.

SHA-256 rather than a slow KDF, deliberately: the secret is 32 bytes straight from
RandomNumberGenerator, so there is no dictionary to search and nothing for an expensive hash to
slow down, while client tokens last an hour and a whole fleet re-authenticates hourly. This is the
same reasoning that already puts invitation tokens on plain SHA-256.

CONVERT(VARCHAR(255), ...) is load-bearing, not tidying. HASHBYTES over an NVARCHAR hashes UTF-16LE
bytes, while the C# side hashes Encoding.UTF8.GetBytes. The secrets are base64 and therefore pure
ASCII, so the VARCHAR conversion makes the two byte-identical. Without it every client on the
platform fails to authenticate at once, and the plaintext needed to diagnose it is already gone.
ClientSecretHashingIntegrationTests pins the two against each other.

The guard is on Secret still existing, not on SecretHash being absent. The three steps are not
atomic, so a run that dies after adding SecretHash leaves a database where it exists but is
partly NULL; guarding on its absence would skip the backfill on the re-run and let the diff drop
the plaintext with nothing hashed. Recomputing every row from Secret is idempotent, so re-running
the whole block is always safe for as long as Secret is there to read.
*/
IF COL_LENGTH('dbo.Clients', 'Secret') IS NOT NULL
BEGIN
    PRINT '>>> Clients: backfilling SecretHash from Secret';

    IF COL_LENGTH('dbo.Clients', 'SecretHash') IS NULL
        EXEC sp_executesql N'ALTER TABLE dbo.Clients ADD SecretHash VARBINARY(32) NULL;';

    EXEC sp_executesql N'UPDATE dbo.Clients
                         SET SecretHash = HASHBYTES(''SHA2_256'', CONVERT(VARCHAR(255), Secret));';

    -- Only after every row has one, or the column cannot take the constraint the table defines.
    EXEC sp_executesql N'ALTER TABLE dbo.Clients ALTER COLUMN SecretHash VARBINARY(32) NOT NULL;';

    PRINT '>>> Clients: backfill complete; the schema diff will now drop Secret';
END
ELSE
    PRINT '>>> Clients: no Secret column to migrate';
GO
/*
--------------------------------------------------------------------------------------
Pre-Deployment Script: drop Client_Verify
--------------------------------------------------------------------------------------
Client_Verify was replaced by Client_GetCredentialBy_RefId when secrets moved to SecretHash, and
its file removed from the project. migrate.sh leaves DropObjectsNotInSource at its default of False, so the
diff never removes it: every migrated database would keep a procedure that reads the dropped
Clients.Secret column. Nothing calls it, and leaving it invites a later publish to fail validating
it. Dropped here explicitly, which is a no-op on a fresh database.
*/
DROP PROCEDURE IF EXISTS dbo.Client_Verify;
GO
/*
--------------------------------------------------------------------------------------
Pre-Deployment Script: drop RegistrationPolicy_Disable
--------------------------------------------------------------------------------------
POST /registration-policies/{ref}/disable went with #352: PATCH sets isEnabled both ways. Nothing
calls the procedure, and DropObjectsNotInSource is off, so it is dropped here as Client_Verify is.
*/
DROP PROCEDURE IF EXISTS dbo.RegistrationPolicy_Disable;
GO
/*
--------------------------------------------------------------------------------------
Pre-Deployment Script: drop DeviceTraffic_InsertBatch and DeviceTraffic_DeleteBefore
--------------------------------------------------------------------------------------
The old names of ClientTraffic_InsertBatch and ClientTraffic_DeleteBefore, kept for one release so
hosts on the previous image kept writing while #585 rolled out (wbskt/Wbskt#586). Nothing calls
them now, and DropObjectsNotInSource is off, so they are dropped here as Client_Verify is.
*/
DROP PROCEDURE IF EXISTS dbo.DeviceTraffic_InsertBatch;
GO
DROP PROCEDURE IF EXISTS dbo.DeviceTraffic_DeleteBefore;
GO
/*
--------------------------------------------------------------------------------------
Pre-Deployment Script: drop HistoryEvent_DeleteForRetiredRuns
--------------------------------------------------------------------------------------
HistoryRetentionGc no longer deletes routine history apart from its run (#774), so nothing calls it
and DropObjectsNotInSource is off: dropped here as Client_Verify is. An engine still on the previous
image fails each hourly retention tick on it, retiring no runs, until it is replaced; the rollout is
short and the next tick on the new image catches up.
*/
DROP PROCEDURE IF EXISTS dbo.HistoryEvent_DeleteForRetiredRuns;
GO
/*
--------------------------------------------------------------------------------------
Pre-Deployment Script: one ScheduledFires row per (WorkflowDefinitionId, TriggerNodeId)
--------------------------------------------------------------------------------------
The table gains UQ_ScheduledFires_WorkflowDefinitionId_TriggerNodeId. Registering the same version
twice used to add a second row, and the constraint cannot be created while one exists. Keep the
oldest row for each pair, and drop the trigger registrations that pointed at the removed ones
(their key is "schedule:<ScheduledFires.Id>", so nothing would ever match them again).
*/
IF OBJECT_ID('dbo.ScheduledFires', 'U') IS NOT NULL
   AND OBJECT_ID('dbo.UQ_ScheduledFires_WorkflowDefinitionId_TriggerNodeId', 'UQ') IS NULL
BEGIN
    EXEC sp_executesql N'
        DECLARE @RemovedFires TABLE (Id INT NOT NULL PRIMARY KEY);

        DELETE sf
        OUTPUT deleted.Id INTO @RemovedFires (Id)
        FROM dbo.ScheduledFires sf
        WHERE EXISTS (SELECT 1
                      FROM dbo.ScheduledFires keep
                      WHERE keep.WorkflowDefinitionId = sf.WorkflowDefinitionId
                        AND keep.TriggerNodeId = sf.TriggerNodeId
                        AND keep.Id < sf.Id);

        IF OBJECT_ID(''dbo.TriggerRegistrations'', ''U'') IS NOT NULL
            DELETE tr
            FROM dbo.TriggerRegistrations tr
            JOIN @RemovedFires r ON tr.TriggerKey = N''schedule:'' + CAST(r.Id AS NVARCHAR(20));

        DECLARE @Removed INT = (SELECT COUNT(*) FROM @RemovedFires);
        PRINT ''>>> ScheduledFires: removed '' + CAST(@Removed AS NVARCHAR(20)) + '' duplicate schedule(s)'';';
END
GO
/*
--------------------------------------------------------------------------------------
Pre-Deployment Script: settle unknown statuses before the Status CHECK constraints
--------------------------------------------------------------------------------------
Runs, Branches and IdempotencyKeys gain CHECK constraints listing the statuses the engine writes
(RunStatus, BranchStatus, IdempotencyKeyStatus). The constraints are created WITH CHECK, so one row
with any other word would stop the deploy. No code path writes another word, and the engine could
not resume such a row anyway, so a row like that is marked Failed: terminal, and visible as a failure
rather than silently gone. Each runs once, while its constraint does not exist yet.
*/
IF OBJECT_ID('dbo.Runs', 'U') IS NOT NULL AND OBJECT_ID('dbo.CK_Runs_Status', 'C') IS NULL
BEGIN
    EXEC sp_executesql N'
        UPDATE dbo.Runs
        SET Status = N''Failed'', CompletedAt = COALESCE(CompletedAt, SYSUTCDATETIME())
        WHERE Status NOT IN (N''Running'', N''Failing'', N''Cancelling'', N''Succeeded'', N''Failed'',
                             N''PartiallyFailed'', N''Cancelled'', N''OutOfCredits'');
        PRINT ''>>> Runs: marked '' + CAST(@@ROWCOUNT AS NVARCHAR(20)) + '' run(s) with an unknown status Failed'';';
END
GO
IF OBJECT_ID('dbo.Branches', 'U') IS NOT NULL AND OBJECT_ID('dbo.CK_Branches_Status', 'C') IS NULL
BEGIN
    EXEC sp_executesql N'
        UPDATE dbo.Branches
        SET Status = N''Failed''
        WHERE Status NOT IN (N''Active'', N''Waiting'', N''Completed'', N''Failed'', N''Cancelled'');
        PRINT ''>>> Branches: marked '' + CAST(@@ROWCOUNT AS NVARCHAR(20)) + '' branch(es) with an unknown status Failed'';';
END
GO
IF OBJECT_ID('dbo.IdempotencyKeys', 'U') IS NOT NULL AND OBJECT_ID('dbo.CK_IdempotencyKeys_Status', 'C') IS NULL
BEGIN
    EXEC sp_executesql N'
        UPDATE dbo.IdempotencyKeys
        SET Status = N''Failed''
        WHERE Status NOT IN (N''Pending'', N''Succeeded'', N''Failed'');
        PRINT ''>>> IdempotencyKeys: marked '' + CAST(@@ROWCOUNT AS NVARCHAR(20)) + '' key(s) with an unknown status Failed'';';
END
GO
/*
--------------------------------------------------------------------------------------
Pre-Deployment Script: drop the month-end credit grant settlement
--------------------------------------------------------------------------------------
Credits past a plan's allowance are drawn from a tenant's grants as they are posted
(dbo.TenantCreditGrants_Draw, #680) instead of an hour into the next month, which used the plan held
at settle time and could draw a whole month from packs after a lapse on the 1st. Nothing calls the
settlement procedures and nothing reads their table, and DropObjectsNotInSource is off, so they are
dropped here as Client_Verify is. What earlier settlements drew stays drawn on the grants.
*/
DROP PROCEDURE IF EXISTS dbo.TenantCreditGrants_Settle;
GO
DROP PROCEDURE IF EXISTS dbo.TenantCreditGrants_GetUnsettled;
GO
DROP TABLE IF EXISTS dbo.TenantCreditGrantSettlements;
GO
/*
--------------------------------------------------------------------------------------
Pre-Deployment Script: workflow state moves to dbo.Workflows
--------------------------------------------------------------------------------------
A workflow's state (Enabled, Disabled, Deprecated, Deleted) and its one live version now live on a
dbo.Workflows row, replacing WorkflowDefinitions.IsEnabled and the dbo.WorkflowDeletions tombstones.
The state is read here, before the diff, into
dbo.WorkflowStateMigration; the post-deployment script fills dbo.Workflows from it and drops it.

  - a tombstone                                  -> Deleted
  - newest version enabled                       -> Enabled, that version live
  - newest version not enabled                   -> Disabled (today's deprecated workflows: they
                                                    become editable again, nothing starts them)

Only the live version keeps trigger registrations and schedules, so whatever an older or a retired
version still had is removed here. Queued events stay: with no registration left a replay matches
nothing, and the reaper expires the rest, while deleting them would fail the upgrade rehearsal's
row-count check on the seeded queue. Schedule
registrations are keyed by workflow and trigger node now ("schedule:<workflowRefId>:<nodeId>")
rather than by the ScheduledFires row, which is replaced on every enable.

Guarded on dbo.WorkflowDeletions still existing (the post-deployment script drops it once
dbo.Workflows is filled), so it runs once and never on a fresh database. IsEnabled itself stays in
the table, unused: dropping it would trip the deploy's data-loss guard (#898).
*/
IF OBJECT_ID('dbo.WorkflowDeletions', 'U') IS NOT NULL
   AND COL_LENGTH('dbo.WorkflowDefinitions', 'IsEnabled') IS NOT NULL
BEGIN
    PRINT '>>> Workflows: capturing workflow state into dbo.Workflows';

    EXEC sp_executesql N'
        IF OBJECT_ID(''dbo.WorkflowStateMigration'', ''U'') IS NOT NULL
            DROP TABLE dbo.WorkflowStateMigration;

        CREATE TABLE dbo.WorkflowStateMigration
        (
            RefId            UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
            WorkspaceId      INT              NOT NULL,
            State            TINYINT          NOT NULL,
            LiveDefinitionId INT              NULL,
            StateChangedBy   INT              NOT NULL,
            StateChangedAt   DATETIME2(3)     NOT NULL,
            CreatedAt        DATETIME2(3)     NOT NULL
        );

        WITH Newest AS (
            SELECT WD.RefId, WD.WorkspaceId, WD.Id, WD.IsEnabled, WD.PublishedBy, WD.CreatedAt,
                   ROW_NUMBER() OVER (PARTITION BY WD.RefId ORDER BY WD.Version DESC) AS Rn,
                   MIN(WD.CreatedAt) OVER (PARTITION BY WD.RefId) AS FirstCreatedAt
            FROM dbo.WorkflowDefinitions WD
            WHERE WD.Kind = N''Version''
        )
        INSERT INTO dbo.WorkflowStateMigration (RefId, WorkspaceId, State, LiveDefinitionId, StateChangedBy, StateChangedAt, CreatedAt)
        SELECT N.RefId,
               N.WorkspaceId,
               CASE WHEN D.RefId IS NOT NULL THEN 4 WHEN N.IsEnabled = 1 THEN 1 ELSE 2 END,
               CASE WHEN D.RefId IS NULL AND N.IsEnabled = 1 THEN N.Id END,
               COALESCE(D.DeletedBy, N.PublishedBy),
               COALESCE(D.DeletedAt, N.CreatedAt),
               N.FirstCreatedAt
        FROM Newest N
        LEFT JOIN dbo.WorkflowDeletions D ON D.RefId = N.RefId
        WHERE N.Rn = 1;

        DELETE TR
        FROM dbo.TriggerRegistrations TR
        WHERE NOT EXISTS (SELECT 1 FROM dbo.WorkflowStateMigration M WHERE M.LiveDefinitionId = TR.WorkflowDefinitionId);

        DELETE SF
        FROM dbo.ScheduledFires SF
        WHERE NOT EXISTS (SELECT 1 FROM dbo.WorkflowStateMigration M WHERE M.LiveDefinitionId = SF.WorkflowDefinitionId);

        UPDATE TR
        SET TriggerKey = N''schedule:'' + LOWER(CONVERT(NVARCHAR(36), SF.WorkflowRefId)) + N'':'' + LOWER(CONVERT(NVARCHAR(36), SF.TriggerNodeId))
        FROM dbo.TriggerRegistrations TR
        JOIN dbo.ScheduledFires SF ON TR.TriggerKey = N''schedule:'' + CAST(SF.Id AS NVARCHAR(20));

        DECLARE @Count INT = (SELECT COUNT(*) FROM dbo.WorkflowStateMigration);
        PRINT ''>>> Workflows: captured the state of '' + CAST(@Count AS NVARCHAR(20)) + '' workflow(s)'';';
END
GO
DROP PROCEDURE IF EXISTS dbo.WorkflowDefinition_UpdateIsEnabled;
GO
DROP PROCEDURE IF EXISTS dbo.TriggerRegistration_Insert;
GO
DROP PROCEDURE IF EXISTS dbo.TriggerRegistration_DeleteAllBy_WorkflowDefinitionId;
GO
DROP PROCEDURE IF EXISTS dbo.ScheduledFire_Insert;
GO
DROP PROCEDURE IF EXISTS dbo.ScheduledFire_DeleteAllBy_WorkflowDefinitionId;
GO
/*
--------------------------------------------------------------------------------------
Pre-Deployment Script: drop procedures that left the project or lost their caller
--------------------------------------------------------------------------------------
ClientReadings_DeleteBefore, ClientStateVariable_Upsert and TriggerDispatchOutcome_Record were
removed from the project (replaced by the plan-based readings sweep with its hourly roll-up,
ClientStateVariables_Report and TriggerDispatchOutcome_RecordMany), and WorkflowDefinition_FindBy_RefId
has no caller. DropObjectsNotInSource is off, so an upgraded database kept them while a fresh one
did not: dropped here as Client_Verify is. ClientReadings_DeleteBefore is the one that mattered: run
by hand it deleted readings by one global age, skipping the roll-up.
*/
DROP PROCEDURE IF EXISTS dbo.ClientReadings_DeleteBefore;
GO
DROP PROCEDURE IF EXISTS dbo.ClientStateVariable_Upsert;
GO
DROP PROCEDURE IF EXISTS dbo.TriggerDispatchOutcome_Record;
GO
DROP PROCEDURE IF EXISTS dbo.WorkflowDefinition_FindBy_RefId;
GO
