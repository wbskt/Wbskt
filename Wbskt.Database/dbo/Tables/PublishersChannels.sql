CREATE TABLE [dbo].[PublishersChannels] (
    [PublisherId]   INT     NOT NULL,
    [ChannelId]     INT     NOT NULL,
    CONSTRAINT [Unq_PublishersChannels]             UNIQUE  NONCLUSTERED    ([PublisherId] ASC, [ChannelId] ASC),
    CONSTRAINT [Fk_PublishersChannels_Publishers]   FOREIGN KEY             ([PublisherId]) REFERENCES [dbo].[Publishers]   ([Id]),
    CONSTRAINT [Fk_PublishersChannels_Channels]     FOREIGN KEY             ([ChannelId])   REFERENCES [dbo].[Channels]     ([Id])
);
