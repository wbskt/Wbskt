CREATE TABLE [dbo].[Publishers] (
    [Id]                INT                 NOT NULL,
    [UserId]            INT                 NOT NULL,
    [PublisherRef]       UNIQUEIDENTIFIER    NOT NULL,
    [PublisherName]     VARCHAR(200)        NOT NULL,
    CONSTRAINT [Fk_Publishers_Users]                    FOREIGN KEY ([UserId])      REFERENCES [dbo].[Users]    ([Id]),
    CONSTRAINT [Pk_Publishers]                          PRIMARY KEY CLUSTERED       ([Id]                   ASC),
    CONSTRAINT [Unq_Publishers_PublisherRef]             UNIQUE      NONCLUSTERED    ([PublisherId]          ASC),
    CONSTRAINT [Unq_Publishers_UserId_PublisherName]    UNIQUE      NONCLUSTERED    ([UserId]               ASC,    [ChannelName]  ASC)
);
