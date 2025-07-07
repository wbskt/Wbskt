/*
    Table: dbo.Publishers
    Purpose: Stores publisher information, including user association and unique reference.
    Columns:
        - Id: INT, primary key
        - UserId: INT, foreign key to Users
        - PublisherRef: UNIQUEIDENTIFIER, unique publisher reference
        - Name: VARCHAR(200), publisher name
        - LastModified: DATETIME, last modification timestamp
    Constraints: PK, unique, FK to Users
    Author: Richard Joy
    Date: 2025-04-25
    Last Modified: 2025-04-25 by Richard Joy - Initial version
*/
CREATE TABLE [dbo].[Publishers] (
    [Id]            INT                 IDENTITY (1, 1) NOT NULL,
    [UserId]        INT                 NOT NULL,
    [PublisherRef]  UNIQUEIDENTIFIER    NOT NULL,
    [Name]          VARCHAR(200)        NOT NULL,
    [LastModified]  DATETIME            DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT [Fk_Publishers_Users]            FOREIGN KEY                 ([UserId])  REFERENCES [dbo].[Users]    ([Id]),
    CONSTRAINT [Pk_Publishers]                  PRIMARY KEY CLUSTERED       ([Id]           ASC),
    CONSTRAINT [Unq_Publishers_PublisherRef]    UNIQUE      NONCLUSTERED    ([PublisherRef] ASC),
    CONSTRAINT [Unq_Publishers_UserId_Name]     UNIQUE      NONCLUSTERED    ([UserId]       ASC,    [Name]  ASC)
);
GO

-- Additional indexes for better performance
CREATE NONCLUSTERED INDEX [IX_Publishers_UserId] ON [dbo].[Publishers] ([UserId] ASC);
GO

CREATE NONCLUSTERED INDEX [IX_Publishers_LastModified] ON [dbo].[Publishers] ([LastModified] ASC);
