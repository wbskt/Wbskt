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

-- Users.IsEmailVerified: added here rather than left to the schema compare so that existing accounts
-- can be backfilled in the same step that creates the column.
--
-- The column defaults to 0, because that is right for every account created from now on. Accounts
-- that already existed were created when registration proved nothing about the address, so switching
-- sign-in to require verification would lock every one of them out of an account they have been
-- using. They are set to 1 exactly once, here, at the moment the column appears.
--
-- Guarded on the table existing as well as the column, because on a fresh database this script runs
-- before Users.sql has created anything.
IF OBJECT_ID('dbo.Users', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Users') AND name = 'IsEmailVerified')
BEGIN
    PRINT 'Adding dbo.Users.IsEmailVerified and backfilling existing accounts to verified.';
    -- Both statements go through EXEC: the DACPAC tool parses this script when it builds the model,
    -- and a literal ALTER TABLE ... ADD here makes it try to model the change (SQL70645). The UPDATE
    -- needs it regardless, to parse before the column exists.
    EXEC('ALTER TABLE dbo.Users ADD IsEmailVerified BIT NOT NULL CONSTRAINT DF_Users_IsEmailVerified DEFAULT 0;');
    EXEC('UPDATE dbo.Users SET IsEmailVerified = 1;');
END
GO
