IF NOT EXISTS (SELECT 1 FROM [dbo].[TriggerTypes] WHERE Id = 1)
BEGIN
    INSERT INTO [dbo].[TriggerTypes] (Id, Name, Description) VALUES (1, 'Manual', 'Triggered by a direct user API call.');
END
GO

IF NOT EXISTS (SELECT 1 FROM [dbo].[TriggerTypes] WHERE Id = 2)
BEGIN
    INSERT INTO [dbo].[TriggerTypes] (Id, Name, Description) VALUES (2, 'Timed', 'Triggered on a recurring CRON schedule.');
END
GO

IF NOT EXISTS (SELECT 1 FROM [dbo].[TriggerTypes] WHERE Id = 3)
BEGIN
    INSERT INTO [dbo].[TriggerTypes] (Id, Name, Description) VALUES (3, 'Webhook', 'Triggered by an incoming HTTP request from an external service.');
END
GO

IF NOT EXISTS (SELECT 1 FROM [dbo].[TriggerTypes] WHERE Id = 4)
BEGIN
    INSERT INTO [dbo].[TriggerTypes] (Id, Name, Description) VALUES (4, 'ClientData', 'Triggered by a data packet received from a connected client.');
END
GO