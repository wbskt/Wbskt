/*
    Table: dbo.ClientsChannels
    Purpose: Stores relationships between clients and channels, including deletion status.
    Columns:
        - ClientId: INT, foreign key to Clients
        - ChannelId: INT, foreign key to Channels
        - LastModified: DATETIME, last modification timestamp
        - Deleted: BIT, soft delete flag
    Constraints: unique, FK to Clients and Channels
    Author: Richard Joy
    Date: 2025-04-25
    Last Modified: 2025-04-25 by Richard Joy - Initial version
*/
CREATE TABLE [dbo].[ClientsChannels] (
    [ClientId]      INT     NOT NULL,
    [ChannelId]     INT     NOT NULL,
    [LastModified]  DATETIME    DEFAULT CURRENT_TIMESTAMP,
    [Deleted]       BIT     NOT NULL,
    CONSTRAINT [Unq_ClientsChannels]            UNIQUE  NONCLUSTERED    ([ChannelId] ASC,   [ClientId] ASC),
    CONSTRAINT [Fk_ClientsChannels_Clients]     FOREIGN KEY             ([ClientId])   REFERENCES [dbo].[Clients]   ([Id]),
    CONSTRAINT [Fk_ClientsChannels_Channels]    FOREIGN KEY             ([ChannelId])  REFERENCES [dbo].[Channels]  ([Id])
);
GO

-- Additional indexes for better performance
CREATE NONCLUSTERED INDEX [IX_ClientsChannels_ClientId_Deleted] ON [dbo].[ClientsChannels] ([ClientId] ASC, [Deleted] ASC);
GO

CREATE NONCLUSTERED INDEX [IX_ClientsChannels_ChannelId_Deleted] ON [dbo].[ClientsChannels] ([ChannelId] ASC, [Deleted] ASC);
GO

CREATE NONCLUSTERED INDEX [IX_ClientsChannels_LastModified] ON [dbo].[ClientsChannels] ([LastModified] ASC);
