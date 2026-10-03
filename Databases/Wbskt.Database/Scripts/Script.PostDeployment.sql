/*
--------------------------------------------------------------------------------------
Post-Deployment Script
--------------------------------------------------------------------------------------
*/

-- 1. Retire the registration policies this script used to seed.
-- Every deploy created PIN 123456 (auto-approve, 100 devices) and PIN 654321 in workspace 1. The
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
-- host-scoped WHERE clauses in Client_UpdatePresence/Client_ResetAllPresence (@HostId = ConnectedHostId)
-- can never match. Left alone, such a client would show IsConnected = 1 forever the moment its
-- pre-migration connection ends without a clean disconnect. Clear those rows once; any client
-- that is actually still connected reconnects and gets a real ConnectedHostId immediately.
UPDATE dbo.Clients
    SET IsConnected = 0,
        ConnectedAt = NULL
    WHERE IsConnected = 1
        AND ConnectedHostId IS NULL;
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
