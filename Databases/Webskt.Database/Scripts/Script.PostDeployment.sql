/*
Post-Deployment Script Template							
--------------------------------------------------------------------------------------
*/

IF NOT EXISTS (SELECT 1 FROM dbo.RegistrationPolicies WHERE Pin = '123456')
BEGIN
    INSERT INTO dbo.RegistrationPolicies (Pin, Name, MaxClients, AutoApproval)
    VALUES ('123456', 'Default Testing Policy', 100, 1);
END
GO
