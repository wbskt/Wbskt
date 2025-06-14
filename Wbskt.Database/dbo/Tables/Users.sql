CREATE TABLE [dbo].[Users] (
    [Id]            INT           IDENTITY (1, 1) NOT NULL,
    [Name]          VARCHAR (100) NOT NULL,
    [EmailId]       VARCHAR (100) NOT NULL,
    [PasswordHash]  VARCHAR (512) NOT NULL,
    [PasswordSalt]  VARCHAR (50)  NOT NULL,
    CONSTRAINT [Pk_Users]           PRIMARY KEY     CLUSTERED       ([Id]       ASC),
    CONSTRAINT [Unq_Users_EmailId]  UNIQUE          NONCLUSTERED    ([EmailId]  ASC)
);
