/*
--------------------------------------------------------------------------------------
Post-Deployment Script: Seed Default Policies
--------------------------------------------------------------------------------------
*/

-- 1. Seed Registration Policies
-- Default Testing Policy
IF NOT EXISTS (SELECT 1 FROM dbo.RegistrationPolicies WHERE Pin = '123456')
    BEGIN
        INSERT INTO dbo.RegistrationPolicies (WorkspaceId, Pin, Name, MaxClients, AutoApproval)
        VALUES (1, '123456', 'Default Testing Policy', 100, 1);
    END
GO

-- Manual Approval Policy
IF NOT EXISTS (SELECT 1 FROM dbo.RegistrationPolicies WHERE Pin = '654321')
    BEGIN
        INSERT INTO dbo.RegistrationPolicies (WorkspaceId, Pin, Name, MaxClients, AutoApproval)
        VALUES (1, '654321', 'Manual Approval Policy', 10, 0);
    END
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