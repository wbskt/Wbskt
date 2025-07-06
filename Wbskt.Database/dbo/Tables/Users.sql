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
