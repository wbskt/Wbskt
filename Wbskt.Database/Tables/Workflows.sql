CREATE TABLE [dbo].[Workflows] (
    [Id] INT IDENTITY(1,1) NOT NULL,
    [RefId] UNIQUEIDENTIFIER NOT NULL,
    [UserId] INT NOT NULL,
    [Name] NVARCHAR(100) NOT NULL,
    [Description] NVARCHAR(500) NULL,
    [IsEnabled] BIT NOT NULL DEFAULT 0,
    [TriggerType] INT NOT NULL,
    [TriggerConfiguration] NVARCHAR(MAX) NULL, -- Stores JSON data like a CRON schedule
    [ViewportX] FLOAT NULL,
    [ViewportY] FLOAT NULL,
    [ViewportZoom] FLOAT NULL,
    [LastModified] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    CONSTRAINT [PK_Workflows] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [UNQ_Workflows_RefId] UNIQUE NONCLUSTERED ([RefId] ASC),
    CONSTRAINT [FK_Workflows_Users] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users]([Id]),
    CONSTRAINT [FK_Workflows_TriggerTypes] FOREIGN KEY ([TriggerType]) REFERENCES [dbo].[TriggerTypes]([Id])
);
