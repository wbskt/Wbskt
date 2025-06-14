CREATE TABLE [dbo].[ClientChannels] (
    [ClientId]      INT     NOT NULL,
    [ChannelId]     INT     NOT NULL,
    CONSTRAINT [Unq_ClientConnectionsChannels]                  UNIQUE  NONCLUSTERED    ([ChannelId] ASC,   [ClientId] ASC),
    CONSTRAINT [Fk_ClientConnectionsChannels_ClientConnections] FOREIGN KEY             ([ClientId])   REFERENCES [dbo].[ClientConnections] ([Id]),
    CONSTRAINT [Fk_ClientConnectionsChannels_Channels]          FOREIGN KEY             ([ChannelId])  REFERENCES [dbo].[Channels]          ([Id])
);
