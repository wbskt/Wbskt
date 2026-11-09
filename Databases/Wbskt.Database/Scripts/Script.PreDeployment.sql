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
