CREATE TABLE [dbo].[Publishers] (
    [Id]            INT                 IDENTITY (1, 1) NOT NULL,
    [UserId]        INT                 NOT NULL,
    [PublisherRef]  UNIQUEIDENTIFIER    NOT NULL,
    [Name]          VARCHAR(200)        NOT NULL,
    [LastModified]  DATETIME            DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT [Fk_Publishers_Users]            FOREIGN KEY                 ([UserId])  REFERENCES [dbo].[Users]    ([Id]),
    CONSTRAINT [Pk_Publishers]                  PRIMARY KEY CLUSTERED       ([Id]           ASC),
    CONSTRAINT [Unq_Publishers_PublisherRef]    UNIQUE      NONCLUSTERED    ([PublisherRef] ASC),
    CONSTRAINT [Unq_Publishers_UserId_Name]     UNIQUE      NONCLUSTERED    ([UserId]       ASC,    [Name]  ASC)
);

-- Additional indexes for better performance
CREATE NONCLUSTERED INDEX [IX_Publishers_UserId] ON [dbo].[Publishers] ([UserId] ASC);
CREATE NONCLUSTERED INDEX [IX_Publishers_LastModified] ON [dbo].[Publishers] ([LastModified] ASC);
