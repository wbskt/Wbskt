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