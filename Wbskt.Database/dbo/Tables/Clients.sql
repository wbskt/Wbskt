CREATE TABLE [dbo].[ClientConnections] (
    [Id]        INT                 IDENTITY (1, 1) NOT NULL,
    [UserId]    INT                 NOT NULL,
    [ServerId]  INT                 NOT NULL,
    [Name]      VARCHAR (100)       NOT NULL,
    [UniqueRef] UNIQUEIDENTIFIER    NOT NULL,
    CONSTRAINT [Pk_ClientsConnections]          PRIMARY KEY CLUSTERED       ([Id]           ASC),
    CONSTRAINT [Unq_UniqueRef]                  UNIQUE      NONCLUSTERED    ([UniqueRef]    ASC),
    CONSTRAINT [Unq_Name_UserId]                UNIQUE      NONCLUSTERED    ([Name]         ASC,    [UserId]    ASC),
    CONSTRAINT [Fk_ClientsConnections_Users]    FOREIGN KEY                 ([UserId])      REFERENCES  [dbo].[Users]   ([Id]),
    CONSTRAINT [Fk_ClientsConnections_Servers]  FOREIGN KEY                 ([ServerId])    REFERENCES  [dbo].[Servers] ([Id])
);
