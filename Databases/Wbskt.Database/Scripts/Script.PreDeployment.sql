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