CREATE TABLE [dbo].[PublishersChannels] (
    [PublisherId]   INT     NOT NULL,
    [ChannelId]     INT     NOT NULL,
    [LastModified]  DATETIME    DEFAULT CURRENT_TIMESTAMP,
    [Deleted]       BIT     NOT NULL,
    CONSTRAINT [Unq_PublishersChannels]             UNIQUE  NONCLUSTERED    ([PublisherId]  ASC, [ChannelId] ASC),
    CONSTRAINT [Fk_PublishersChannels_Publishers]   FOREIGN KEY             ([PublisherId]) REFERENCES [dbo].[Publishers]   ([Id]),
    CONSTRAINT [Fk_PublishersChannels_Channels]     FOREIGN KEY             ([ChannelId])   REFERENCES [dbo].[Channels]     ([Id])
);

-- Additional indexes for better performance
CREATE NONCLUSTERED INDEX [IX_PublishersChannels_PublisherId_Deleted] ON [dbo].[PublishersChannels] ([PublisherId] ASC, [Deleted] ASC);
CREATE NONCLUSTERED INDEX [IX_PublishersChannels_ChannelId_Deleted] ON [dbo].[PublishersChannels] ([ChannelId] ASC, [Deleted] ASC);
CREATE NONCLUSTERED INDEX [IX_PublishersChannels_LastModified] ON [dbo].[PublishersChannels] ([LastModified] ASC);
