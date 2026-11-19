/*
--------------------------------------------------------------------------------------
Post-Deployment Script
--------------------------------------------------------------------------------------
*/

-- 1. Retire the registration policies this script used to seed.
-- Every deploy created PIN 123456 (auto-approve, 100 clients) and PIN 654321 in workspace 1. The
-- registration endpoint is anonymous, so a PIN anyone would guess first was an open door into the
-- default workspace. The seed is gone; this disables the rows already created. Disabling, not
-- deleting: clients registered through them reference the policy and keep working. It runs on every
-- deploy, so re-enabling one from the console does not stick; create a new policy (which gets a
-- generated PIN) instead. Matched on name as well as PIN so that a policy an administrator created
-- with the same PIN is left alone.
UPDATE dbo.RegistrationPolicies
    SET IsEnabled = 0
    WHERE WorkspaceId = 1
        AND IsEnabled = 1
        AND ((Pin = '123456' AND Name = 'Default Testing Policy')
          OR (Pin = '654321' AND Name = 'Manual Approval Policy'));
GO

-- 2. One-time fix-up for the horizontal-scale migration (added ConnectedHostId).
-- Rows that were connected before this column existed have ConnectedHostId = NULL, which the
-- host-scoped WHERE clauses in Client_UpdatePresence/Client_ResetPresenceBy_HostId (@HostId = ConnectedHostId)
-- can never match. Left alone, such a client would show IsConnected = 1 forever the moment its
-- pre-migration connection ends without a clean disconnect. Clear those rows once; any client
-- that is actually still connected reconnects and gets a real ConnectedHostId immediately.
UPDATE dbo.Clients
    SET IsConnected = 0,
        ConnectedAt = NULL
    WHERE IsConnected = 1
        AND ConnectedHostId IS NULL;
GO

-- 3. Move dbo.EventLogIds past the ids already used.
-- dbo.EventLogs.Id was an IDENTITY until client traffic moved to dbo.ClientTraffic and the two began
-- sharing this sequence. The schema change keeps the existing ids, and the sequence starts at 1, so
-- without this the next insert would collide with the oldest row. It moves only when the highest id
-- is past the last one the sequence handed out, so it runs on the first deploy over existing rows and
-- is a no-op after that and on a fresh database. Rows written by a host in the moment between the
-- table rebuild and this script fail their insert and are retried by the event logger, not lost.
-- Advanced by taking a range rather than ALTER SEQUENCE ... RESTART, which changes the start value:
-- sqlpackage would see that as drift from the model and restart the sequence at 1 on every publish.
IF EXISTS (
    SELECT 1
    FROM sys.sequences AS s
    WHERE s.object_id = OBJECT_ID(N'dbo.EventLogIds')
      AND ISNULL(CAST(s.last_used_value AS BIGINT), 0) < (
          SELECT MAX(Id)
          FROM (
              SELECT ISNULL(MAX(Id), 0) AS Id FROM dbo.EventLogs
              UNION ALL
              SELECT ISNULL(MAX(Id), 0) FROM dbo.ClientTraffic
          ) AS Highest))
BEGIN
    DECLARE @EventLogIdGap BIGINT = (
        SELECT MAX(Id)
        FROM (
            SELECT ISNULL(MAX(Id), 0) AS Id FROM dbo.EventLogs
            UNION ALL
            SELECT ISNULL(MAX(Id), 0) FROM dbo.ClientTraffic
        ) AS Highest)
        - (SELECT ISNULL(CAST(last_used_value AS BIGINT), 0) FROM sys.sequences WHERE object_id = OBJECT_ID(N'dbo.EventLogIds'));
    PRINT '>>> Advancing dbo.EventLogIds by ' + CAST(@EventLogIdGap AS NVARCHAR(20));
    DECLARE @EventLogIdRangeFirst SQL_VARIANT;
    EXEC sys.sp_sequence_get_range
        @sequence_name = N'dbo.EventLogIds',
        @range_size = @EventLogIdGap,
        @range_first_value = @EventLogIdRangeFirst OUTPUT;
END
GO

-- Login for the management host: [wbskt_management], which may execute this database's procedures and nothing else.
-- Created here, not in the model, because a login is a server object and its password is a secret
-- that varies per environment. The password arrives as the $(ManagementHostPassword) SQLCMD variable; empty (the
-- default) skips this block, which is what local development and the integration suite rely on.
-- Rerunning with a new password rotates it. ALTER USER ... WITH LOGIN re-links the user after a
-- restore onto another server, where the login's SID would otherwise not match.
-- Each statement runs through EXEC so the DACPAC build cannot see it: the SDK lifts a literal
-- CREATE LOGIN, CREATE USER or GRANT out of this script into the model, and sqlpackage would then
-- create the login itself on every publish, failing when the password variable is empty.
IF N'$(ManagementHostPassword)' <> N''
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'wbskt_management')
        EXEC (N'CREATE LOGIN [wbskt_management] WITH PASSWORD = N''$(ManagementHostPassword)'', DEFAULT_DATABASE = [$(DatabaseName)]');
    ELSE
        EXEC (N'ALTER LOGIN [wbskt_management] WITH PASSWORD = N''$(ManagementHostPassword)''');

    IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'wbskt_management')
        EXEC (N'CREATE USER [wbskt_management] FOR LOGIN [wbskt_management]');
    ELSE
        EXEC (N'ALTER USER [wbskt_management] WITH LOGIN = [wbskt_management]');

    -- Every query the hosts make is a stored procedure in dbo (the health check's SELECT 1 needs
    -- nothing), and ownership chaining lets those procedures read and write the tables. A host that
    -- starts sending ad-hoc SQL will fail with a permission error rather than quietly widening this.
    -- CONNECT is granted explicitly rather than relying on CREATE USER to imply it.
    EXEC (N'GRANT CONNECT TO [wbskt_management]');
    EXEC (N'GRANT EXECUTE ON SCHEMA::dbo TO [wbskt_management]');
END
GO

-- Login for the workflow engine host: [wbskt_engine], which may execute this database's procedures and nothing else.
-- Created here, not in the model, because a login is a server object and its password is a secret
-- that varies per environment. The password arrives as the $(EngineHostPassword) SQLCMD variable; empty (the
-- default) skips this block, which is what local development and the integration suite rely on.
-- Rerunning with a new password rotates it. ALTER USER ... WITH LOGIN re-links the user after a
-- restore onto another server, where the login's SID would otherwise not match.
-- Each statement runs through EXEC so the DACPAC build cannot see it: the SDK lifts a literal
-- CREATE LOGIN, CREATE USER or GRANT out of this script into the model, and sqlpackage would then
-- create the login itself on every publish, failing when the password variable is empty.
IF N'$(EngineHostPassword)' <> N''
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'wbskt_engine')
        EXEC (N'CREATE LOGIN [wbskt_engine] WITH PASSWORD = N''$(EngineHostPassword)'', DEFAULT_DATABASE = [$(DatabaseName)]');
    ELSE
        EXEC (N'ALTER LOGIN [wbskt_engine] WITH PASSWORD = N''$(EngineHostPassword)''');

    IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'wbskt_engine')
        EXEC (N'CREATE USER [wbskt_engine] FOR LOGIN [wbskt_engine]');
    ELSE
        EXEC (N'ALTER USER [wbskt_engine] WITH LOGIN = [wbskt_engine]');

    -- Every query the hosts make is a stored procedure in dbo (the health check's SELECT 1 needs
    -- nothing), and ownership chaining lets those procedures read and write the tables. A host that
    -- starts sending ad-hoc SQL will fail with a permission error rather than quietly widening this.
    -- CONNECT is granted explicitly rather than relying on CREATE USER to imply it.
    EXEC (N'GRANT CONNECT TO [wbskt_engine]');
    EXEC (N'GRANT EXECUTE ON SCHEMA::dbo TO [wbskt_engine]');
END
GO

-- Login for the platform admin host (wbskt/Wbskt.Admin): [wbskt_admin_reader], which may read the views
-- in the [admin] schema and nothing else, in this database and, under the same login, in the other one.
-- Created as the per-host logins above are: from the $(AdminReaderPassword) SQLCMD variable, skipped
-- when it is empty, rotated by rerunning with a new one, each statement through EXEC so the DACPAC
-- build does not lift it into the model. No EXECUTE and nothing on dbo: the admin host never writes
-- here, it asks the host that owns the change through an internal admin endpoint.
IF N'$(AdminReaderPassword)' <> N''
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'wbskt_admin_reader')
        EXEC (N'CREATE LOGIN [wbskt_admin_reader] WITH PASSWORD = N''$(AdminReaderPassword)'', DEFAULT_DATABASE = [$(DatabaseName)]');
    ELSE
        EXEC (N'ALTER LOGIN [wbskt_admin_reader] WITH PASSWORD = N''$(AdminReaderPassword)''');

    IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'wbskt_admin_reader')
        EXEC (N'CREATE USER [wbskt_admin_reader] FOR LOGIN [wbskt_admin_reader]');
    ELSE
        EXEC (N'ALTER USER [wbskt_admin_reader] WITH LOGIN = [wbskt_admin_reader]');

    EXEC (N'GRANT CONNECT TO [wbskt_admin_reader]');
    EXEC (N'GRANT SELECT ON SCHEMA::[admin] TO [wbskt_admin_reader]');
END
GO

-- 4. Fill dbo.Workflows from the state the pre-deployment script captured from
-- WorkflowDefinitions.IsEnabled and dbo.WorkflowDeletions, then drop the capture and the tombstone
-- table it replaces (DropObjectsNotInSource is off, so the diff leaves WorkflowDeletions in place).
-- Runs once: the capture exists only on the deploy that still had WorkflowDeletions.
IF OBJECT_ID('dbo.WorkflowStateMigration', 'U') IS NOT NULL
BEGIN
    EXEC sp_executesql N'
        INSERT INTO dbo.Workflows (RefId, WorkspaceId, State, LiveDefinitionId, StateChangedBy, StateChangedAt, CreatedAt)
        SELECT M.RefId, M.WorkspaceId, M.State, M.LiveDefinitionId, M.StateChangedBy, M.StateChangedAt, M.CreatedAt
        FROM dbo.WorkflowStateMigration M
        WHERE NOT EXISTS (SELECT 1 FROM dbo.Workflows W WHERE W.RefId = M.RefId);

        PRINT ''>>> Workflows: created '' + CAST(@@ROWCOUNT AS NVARCHAR(20)) + '' workflow row(s)'';

        DROP TABLE dbo.WorkflowStateMigration;';
END
GO
DROP TABLE IF EXISTS dbo.WorkflowDeletions;
GO

-- 5. Hash the webhook secrets of registrations written before TriggerRegistrations.WebhookSecretHash
-- existed (dbo.TriggerRegistration_BackfillWebhookSecretHash explains how). The dispatcher hashes a
-- missing one itself, so this only stops it doing so per event. A no-op once every row has one.
EXEC dbo.TriggerRegistration_BackfillWebhookSecretHash;
GO

-- 6. Fill Runs.WorkspaceId (#1000 item 197) on the runs started before the column existed. Here, not in
-- the pre-deployment script: sqlpackage plans the ALTER TABLE before that script runs, so a column added
-- there is added twice. Until this runs, the swapped procedures do not see those runs for the few
-- seconds between, and it also catches a run the previous image starts in that time. A no-op after the
-- first deploy; goes with the column's NULL in the release after (#1059).
UPDATE r
SET r.WorkspaceId = d.WorkspaceId
FROM dbo.Runs r
JOIN dbo.WorkflowDefinitions d ON d.Id = r.WorkflowDefinitionId
WHERE r.WorkspaceId IS NULL;
GO
