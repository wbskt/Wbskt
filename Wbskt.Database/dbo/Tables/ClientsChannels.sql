CREATE TABLE [dbo].[ClientsChannels] (
    [ClientId]      INT     NOT NULL,
    [ChannelId]     INT     NOT NULL,
    [LastModified]  DATETIME    DEFAULT CURRENT_TIMESTAMP,
    [Deleted]       BIT     NOT NULL,
    CONSTRAINT [Unq_ClientsChannels]            UNIQUE  NONCLUSTERED    ([ChannelId] ASC,   [ClientId] ASC),
    CONSTRAINT [Fk_ClientsChannels_Clients]     FOREIGN KEY             ([ClientId])   REFERENCES [dbo].[Clients]   ([Id]),
    CONSTRAINT [Fk_ClientsChannels_Channels]    FOREIGN KEY             ([ChannelId])  REFERENCES [dbo].[Channels]  ([Id])
);

-- Additional indexes for better performance
CREATE NONCLUSTERED INDEX [IX_ClientsChannels_ClientId_Deleted] ON [dbo].[ClientsChannels] ([ClientId] ASC, [Deleted] ASC);
CREATE NONCLUSTERED INDEX [IX_ClientsChannels_ChannelId_Deleted] ON [dbo].[ClientsChannels] ([ChannelId] ASC, [Deleted] ASC);
CREATE NONCLUSTERED INDEX [IX_ClientsChannels_LastModified] ON [dbo].[ClientsChannels] ([LastModified] ASC);
