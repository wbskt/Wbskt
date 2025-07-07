/*
    Table: dbo.Users
    Purpose: Stores user account information and credentials.
    Columns:
        - Id: INT, primary key
        - Name: VARCHAR(100), user name
        - EmailId: VARCHAR(100), unique email address
        - PasswordHash: VARCHAR(512), password hash
        - PasswordSalt: VARCHAR(50), password salt
        - LastModified: DATETIME, last modification timestamp
    Constraints: PK, unique on EmailId
    Author: Richard Joy
    Date: 2025-04-25
    Last Modified: 2025-04-25 by Richard Joy - Initial version
*/
CREATE TABLE [dbo].[Users] (
    [Id]            INT             IDENTITY (1, 1) NOT NULL,
    [Name]          VARCHAR (100)   NOT NULL,
    [EmailId]       VARCHAR (100)   NOT NULL,
    [PasswordHash]  VARCHAR (512)   NOT NULL,
    [PasswordSalt]  VARCHAR (50)    NOT NULL,
    [LastModified]  DATETIME        DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT [Pk_Users]           PRIMARY KEY     CLUSTERED       ([Id]       ASC),
    CONSTRAINT [Unq_Users_EmailId]  UNIQUE          NONCLUSTERED    ([EmailId]  ASC)
);
GO

-- Additional indexes for better performance
CREATE NONCLUSTERED INDEX [IX_Users_LastModified] ON [dbo].[Users] ([LastModified] ASC);
