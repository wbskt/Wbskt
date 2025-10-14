INSERT INTO [dbo].[TriggerTypes] (Id, Name, Description) VALUES
    (1, 'Manual', 'Triggered by a direct user API call.'),
    (2, 'Timed', 'Triggered on a recurring CRON schedule.'),
    (3, 'Webhook', 'Triggered by an incoming HTTP request from an external service.'),
    (4, 'ClientData', 'Triggered by a data packet received from a connected client.');
GO