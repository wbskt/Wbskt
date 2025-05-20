CREATE TABLE [dbo].[PublisherChannels] (
    [PublisherRef]      INT     NOT NULL,
    [ChannelId]     INT     NOT NULL,
    CONSTRAINT [Unq_PublisherChannels]  UNIQUE  NONCLUSTERED    ([PublisherRef] ASC,   [ChannelId] ASC),
    CONSTRAINT [Fk_PublisherChannels_Publishers]   FOREIGN KEY     ([PublisherRef])   REFERENCES [dbo].[PublisherChannels]    ([Id]),
    CONSTRAINT [Fk_PublisherChannels_Channels]            FOREIGN KEY     ([ChannelId])  REFERENCES [dbo].[Channels]             ([Id])
);
