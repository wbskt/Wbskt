/*
    Table: dbo.Channels
    Purpose: Stores channel information, including user association and unique reference.
    Columns:
        - Id: INT, primary key
        - Name: VARCHAR(100), channel name
        - UserId: INT, foreign key to Users
        - ChannelRef: UNIQUEIDENTIFIER, unique channel reference
        - LastModified: DATETIME, last modification timestamp
    Constraints: PK, unique, FK to Users
    Author: Richard Joy
    Date: 2025-04-25
    Last Modified: 2025-04-25 by Richard Joy - Initial version
*/
CREATE TABLE [dbo].[Channels] (
    [Id]                INT                 IDENTITY (1, 1) NOT NULL,
    [Name]              VARCHAR (100)       NOT NULL,
    [UserId]            INT                 NOT NULL,
    [ChannelRef]        UNIQUEIDENTIFIER    NOT NULL,
    [LastModified]      DATETIME            DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT [Fk_Channels_Users]  FOREIGN KEY             ([UserId])  REFERENCES [dbo].[Users]    ([Id]),
    CONSTRAINT [Pk_Channels]        PRIMARY KEY CLUSTERED   ([Id]   ASC),
    CONSTRAINT [Unq_Channels_ChannelRef]   UNIQUE      NONCLUSTERED    ([ChannelRef]  ASC),
    CONSTRAINT [Unq_Channels_UserId_Name]       UNIQUE      NONCLUSTERED    ([UserId]           ASC, [Name] ASC)
);
GO

-- Additional indexes for better performance
CREATE NONCLUSTERED INDEX [IX_Channels_UserId] ON [dbo].[Channels] ([UserId] ASC);
GO

CREATE NONCLUSTERED INDEX [IX_Channels_LastModified] ON [dbo].[Channels] ([LastModified] ASC);
