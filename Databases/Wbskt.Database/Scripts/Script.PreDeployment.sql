/*
 Pre-Deployment Script Template							
--------------------------------------------------------------------------------------
 This file contains SQL statements that will be executed before the build script.	
--------------------------------------------------------------------------------------
*/

-- Enable Read Committed Snapshot Isolation (RCSI)
-- This improves concurrency by preventing readers from being blocked by writers.
IF (SELECT is_read_committed_snapshot_on FROM sys.databases WHERE name = DB_NAME()) = 0
BEGIN
    -- We must use a separate batch and force other connections to close
    PRINT 'Enabling READ_COMMITTED_SNAPSHOT for ' + DB_NAME();
    
    DECLARE @sql NVARCHAR(MAX) = 'ALTER DATABASE [' + DB_NAME() + '] SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;';
    EXEC sp_executesql @sql;
END
GO
