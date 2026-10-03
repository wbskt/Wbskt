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
Device secrets used to be stored in plaintext and compared inside Client_Verify. They are now
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
ASCII, so the VARCHAR conversion makes the two byte-identical. Without it every device on the
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
