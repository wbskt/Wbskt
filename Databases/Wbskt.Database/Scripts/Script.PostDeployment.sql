/*
--------------------------------------------------------------------------------------
Post-Deployment Script: Seed Default Policies and Workflows
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

-- 2. Seed Default 'System Alert' Workflow
IF NOT EXISTS (SELECT 1 FROM dbo.Workflows WHERE Name = 'System Alert')
    BEGIN
        INSERT INTO dbo.Workflows (WorkspaceId, Name, Description, IsEnabled, DefinitionJson)
        VALUES (
               1,
               'System Alert',
               'Auto-generated alert for device telemetry.',
               1,
               '{
                 "Nodes": [
                   {
                     "$type": "trigger:device",
                     "NodeId": "de000000-0000-0000-0000-000000000001",
                     "Name": "On Device Message",
                     "ClientRefId": "00000000-0000-0000-0000-000000000000",
                     "TriggerType": 0
                   },
                   {
                     "$type": "action:toast",
                     "NodeId": "ac000000-0000-0000-0000-000000000001",
                     "Name": "Show Alert",
                     "Title": "System Alert",
                     "Message": "New telemetry data received from device."
                   }
                 ],
                 "Edges": [
                   {
                     "EdgeId": "ed000000-0000-0000-0000-000000000001",
                     "Source": { "NodeId": "de000000-0000-0000-0000-000000000001", "PortId": "out" },
                     "Target": { "NodeId": "ac000000-0000-0000-0000-000000000001", "PortId": "in" }
                   }
                 ],
                 "InitialState": {},
                 "Concurrency": 0
               }'
               );
    END
GO