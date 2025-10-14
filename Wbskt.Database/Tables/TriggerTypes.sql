CREATE TABLE [dbo].[TriggerTypes] (
    [Id] INT NOT NULL,
    [Name] VARCHAR(50) NOT NULL,
    [Description] NVARCHAR(250) NULL,
    CONSTRAINT [PK_TriggerTypes] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [UNQ_TriggerTypes_Name] UNIQUE NONCLUSTERED ([Name] ASC)
);
GO
