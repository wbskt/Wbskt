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

-- RefreshTokens.Token -> RefreshTokens.TokenHash: refresh tokens used to be stored in plaintext.
--
-- Existing sessions are carried across rather than dropped. Each row is copied, with its token and
-- its ReplacedByToken hashed, into a scratch table, and the table is then emptied. The schema diff
-- that follows this script therefore sees an empty table: it can drop the plaintext columns and add
-- the NOT NULL hash without tripping BlockOnPossibleDataLoss, and without colliding with anything
-- added here (sqlpackage plans the diff before this script runs; see the IsEmailVerified note above
-- for what happens otherwise). The post-deployment script puts the rows back, Ids included, and
-- drops the scratch table.
--
-- CONVERT(VARCHAR(255), ...) is load-bearing: HASHBYTES over an NVARCHAR hashes UTF-16LE, while
-- SecurityTokens.Hash hashes UTF-8. Refresh tokens are base64 and therefore pure ASCII, so the
-- VARCHAR conversion makes the two byte-identical; without it every carried-over session would fail
-- to refresh. RefreshTokenHashMigrationTests pins the two together.
--
-- Guarded on Token still existing, and idempotent: a run that dies after the copy re-copies only the
-- rows the scratch table does not have yet, and the copy and the delete are one transaction. Through
-- sp_executesql because a fresh database has neither table nor column when this batch is parsed.
IF COL_LENGTH('dbo.RefreshTokens', 'Token') IS NOT NULL
BEGIN
    PRINT 'Carrying dbo.RefreshTokens across the switch to hashed tokens.';

    IF OBJECT_ID('dbo.__RefreshTokenHashBackfill', 'U') IS NULL
        EXEC sp_executesql N'CREATE TABLE dbo.__RefreshTokenHashBackfill (
            Id INT NOT NULL PRIMARY KEY,
            UserId INT NOT NULL,
            TokenHash VARBINARY(32) NOT NULL,
            Expires DATETIME2(3) NOT NULL,
            Revoked DATETIME2(3) NULL,
            CreatedByIp NVARCHAR(50) NULL,
            RevokedByIp NVARCHAR(50) NULL,
            ReplacedByTokenHash VARBINARY(32) NULL);';

    EXEC sp_executesql N'
        SET XACT_ABORT ON;
        BEGIN TRANSACTION;

        INSERT INTO dbo.__RefreshTokenHashBackfill
            (Id, UserId, TokenHash, Expires, Revoked, CreatedByIp, RevokedByIp, ReplacedByTokenHash)
        SELECT R.Id,
               R.UserId,
               HASHBYTES(''SHA2_256'', CONVERT(VARCHAR(255), R.Token)),
               R.Expires,
               R.Revoked,
               R.CreatedByIp,
               R.RevokedByIp,
               CASE WHEN R.ReplacedByToken IS NULL THEN NULL
                    ELSE HASHBYTES(''SHA2_256'', CONVERT(VARCHAR(255), R.ReplacedByToken)) END
        FROM dbo.RefreshTokens R
        WHERE NOT EXISTS (SELECT 1 FROM dbo.__RefreshTokenHashBackfill B WHERE B.Id = R.Id);

        DELETE FROM dbo.RefreshTokens;

        COMMIT TRANSACTION;';
END
GO
