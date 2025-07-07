/*
    Table: dbo.PublishersChannels
    Purpose: Stores relationships between publishers and channels, including deletion status.
    Columns:
        - PublisherId: INT, foreign key to Publishers
        - ChannelId: INT, foreign key to Channels
        - LastModified: DATETIME, last modification timestamp
        - Deleted: BIT, soft delete flag
    Constraints: unique, FK to Publishers and Channels
    Author: Richard Joy
    Date: 2025-04-25
    Last Modified: 2025-04-25 by Richard Joy - Initial version
*/
CREATE TABLE [dbo].[PublishersChannels] (
    [PublisherId]   INT     NOT NULL,
    [ChannelId]     INT     NOT NULL,
    [LastModified]  DATETIME    DEFAULT CURRENT_TIMESTAMP,
    [Deleted]       BIT     NOT NULL,
    CONSTRAINT [Unq_PublishersChannels]             UNIQUE  NONCLUSTERED    ([PublisherId]  ASC, [ChannelId] ASC),
    CONSTRAINT [Fk_PublishersChannels_Publishers]   FOREIGN KEY             ([PublisherId]) REFERENCES [dbo].[Publishers]   ([Id]),
    CONSTRAINT [Fk_PublishersChannels_Channels]     FOREIGN KEY             ([ChannelId])   REFERENCES [dbo].[Channels]     ([Id])
);
GO

-- Additional indexes for better performance
CREATE NONCLUSTERED INDEX [IX_PublishersChannels_PublisherId_Deleted] ON [dbo].[PublishersChannels] ([PublisherId] ASC, [Deleted] ASC);
GO

CREATE NONCLUSTERED INDEX [IX_PublishersChannels_ChannelId_Deleted] ON [dbo].[PublishersChannels] ([ChannelId] ASC, [Deleted] ASC);
GO

CREATE NONCLUSTERED INDEX [IX_PublishersChannels_LastModified] ON [dbo].[PublishersChannels] ([LastModified] ASC);
