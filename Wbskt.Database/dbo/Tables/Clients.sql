CREATE TABLE [dbo].[Clients] (
    [Id]                INT                 IDENTITY (1, 1) NOT NULL,
    [UserId]            INT                 NOT NULL,
    [ServerId]          INT                 NOT NULL,
    [Name]              VARCHAR (100)       NOT NULL,
    [UniqueRef]         UNIQUEIDENTIFIER    NOT NULL,
    [LastModified]      DATETIME            DEFAULT CURRENT_TIMESTAMP,
    [PolicyId]          INT                 NOT NULL,
    CONSTRAINT [Pk_Clients]             PRIMARY KEY CLUSTERED       ([Id]           ASC),
    CONSTRAINT [Unq_UniqueRef]          UNIQUE      NONCLUSTERED    ([UniqueRef]    ASC),
    CONSTRAINT [Unq_Name_UserId]        UNIQUE      NONCLUSTERED    ([Name]         ASC,    [UserId]    ASC),
    CONSTRAINT [Fk_Clients_Users]       FOREIGN KEY                 ([UserId])      REFERENCES  [dbo].[Users]   ([Id]),
    CONSTRAINT [Fk_Clients_Servers]     FOREIGN KEY                 ([ServerId])    REFERENCES  [dbo].[Servers] ([Id]),
    CONSTRAINT [Fk_Clients_Policies]    FOREIGN KEY                 ([PolicyId])    REFERENCES  [dbo].[EnrollmentPolicies] ([Id])
);
GO

-- Additional indexes for better performance
CREATE NONCLUSTERED INDEX [IX_Clients_UserId] ON [dbo].[Clients] ([UserId] ASC);
GO

CREATE NONCLUSTERED INDEX [IX_Clients_ServerId] ON [dbo].[Clients] ([ServerId] ASC);
GO

CREATE NONCLUSTERED INDEX [IX_Clients_LastModified] ON [dbo].[Clients] ([LastModified] ASC);
