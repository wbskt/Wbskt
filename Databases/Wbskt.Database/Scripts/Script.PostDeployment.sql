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