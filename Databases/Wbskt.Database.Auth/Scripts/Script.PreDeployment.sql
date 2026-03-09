/*
 Pre-Deployment Script Template							
--------------------------------------------------------------------------------------
*/

IF (SELECT is_read_committed_snapshot_on FROM sys.databases WHERE name = DB_NAME()) = 0
BEGIN
    PRINT 'Enabling READ_COMMITTED_SNAPSHOT for ' + DB_NAME();
    DECLARE @sql NVARCHAR(MAX) = 'ALTER DATABASE [' + DB_NAME() + '] SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;';
    EXEC sp_executesql @sql;
END
GO
