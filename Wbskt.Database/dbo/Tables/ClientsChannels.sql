CREATE TABLE [dbo].[ClientsChannels] (
    [ClientId]      INT     NOT NULL,
    [ChannelId]     INT     NOT NULL,
    [LastModified]  DATETIME    DEFAULT CURRENT_TIMESTAMP,
    [Deleted]       BIT     NOT NULL,
    CONSTRAINT [Unq_ClientsChannels]            UNIQUE  NONCLUSTERED    ([ChannelId] ASC,   [ClientId] ASC),
    CONSTRAINT [Fk_ClientsChannels_Clients]     FOREIGN KEY             ([ClientId])   REFERENCES [dbo].[Clients]   ([Id]),
    CONSTRAINT [Fk_ClientsChannels_Channels]    FOREIGN KEY             ([ChannelId])  REFERENCES [dbo].[Channels]  ([Id])
);
