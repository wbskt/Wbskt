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

-- Users.IsEmailVerified: existing accounts are backfilled to verified, exactly once.
--
-- The column defaults to 0, because that is right for every account created from now on. Accounts
-- that already existed were created when registration proved nothing about the address, so switching
-- sign-in to require verification would lock every one of them out of an account they have been
-- using.
--
-- The column itself is left to the schema compare. Adding it here instead broke the first publish
-- against any database that predates it: sqlpackage computes its plan before this script runs, so the
-- plan still rebuilt dbo.Users to add the column and collided with the one added here (Msg 2714 on
-- DF_Users_IsEmailVerified), leaving the table's foreign keys dropped. So this script only records
-- which accounts exist at the moment the column is missing, in a scratch table the post-deployment
-- script consumes and drops once the column is there.
--
-- Guarded on the table existing as well as the column, because on a fresh database this script runs
-- before Users.sql has created anything. Everything goes through EXEC so the DACPAC tool does not try
-- to model the scratch table (SQL70645).
IF OBJECT_ID('dbo.Users', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Users') AND name = 'IsEmailVerified')
   AND OBJECT_ID('dbo.__EmailVerifiedBackfill', 'U') IS NULL
BEGIN
    PRINT 'Recording existing accounts to backfill dbo.Users.IsEmailVerified once the column exists.';
    EXEC('SELECT Id INTO dbo.__EmailVerifiedBackfill FROM dbo.Users;');
END
GO
