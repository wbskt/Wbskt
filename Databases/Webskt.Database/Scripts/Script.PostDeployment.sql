/*
Post-Deployment Script Template							
--------------------------------------------------------------------------------------
*/

-- Create the default policy, assuming WorkspaceId=1 exists in the Auth database.
IF NOT EXISTS (SELECT 1 FROM dbo.RegistrationPolicies WHERE Pin = '123456')
BEGIN
    INSERT INTO dbo.RegistrationPolicies (WorkspaceId, Pin, Name, MaxClients, AutoApproval)
    VALUES (1, '123456', 'Default Testing Policy', 100, 1);
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.RegistrationPolicies WHERE Pin = '654321')
BEGIN
    INSERT INTO dbo.RegistrationPolicies (WorkspaceId, Pin, Name, MaxClients, AutoApproval)
    VALUES (1, '654321', 'Manual Approval Policy', 10, 0);
END
GO
