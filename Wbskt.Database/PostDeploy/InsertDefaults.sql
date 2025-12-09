-- Insert Default Root User Script
-- This script inserts a default "root" user with ID 1 if it doesn't exist
-- The password hash is for the password "root" using BCrypt

-- Check if the root user already exists
IF NOT EXISTS (SELECT 1 FROM [dbo].[Users] WHERE [Name] = 'root')
    BEGIN
        -- Insert the root user with a pre-hashed password
        -- Password: "root"
        -- Hash: hash for "root" password
        INSERT INTO [dbo].[Users] ([Name], [PasswordHash], [EmailId], [LastModified])
        VALUES (
                   'root',
                   'JAvlGPq9JyTdtvBO6x2llnRI1+gxwIyPqCKAn3THIKk=',
                   'root@wbskt.com',
                   SYSUTCDATETIME()
               );

    END
ELSE
    BEGIN
        PRINT 'root user already exists, skipping creation';
    END

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