CREATE TABLE [dbo].[Channels] (
    [Id]                    INT              IDENTITY (1, 1) NOT NULL,
    [ChannelName]           VARCHAR (100)    NOT NULL,
    [UserId]                INT              NOT NULL,
    [ChannelSubscriberId]   UNIQUEIDENTIFIER NOT NULL,
    CONSTRAINT [Fk_Channels_Users]      FOREIGN KEY ([UserId])      REFERENCES [dbo].[Users]    ([Id]),
    CONSTRAINT [Pk_Channels]                        PRIMARY KEY CLUSTERED       ([Id]                   ASC),
    CONSTRAINT [Unq_Channels_ChannelSubscriberId]   UNIQUE      NONCLUSTERED    ([ChannelSubscriberId]  ASC),
    CONSTRAINT [Unq_Channels_UserId_ChannelName]    UNIQUE      NONCLUSTERED    ([UserId]               ASC, [ChannelName]  ASC)
);
