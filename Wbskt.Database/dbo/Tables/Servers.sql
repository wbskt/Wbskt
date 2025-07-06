CREATE TABLE [dbo].[Servers] (
    [Id]                INT             IDENTITY (1, 1) NOT NULL,
    [IPAddress]         VARCHAR (100)   NOT NULL,
    [PublicDomainName]  VARCHAR (100)   NOT NULL,
    [Port]              INT             NOT NULL,
    [Type]              INT             NOT NULL,
    [Active]            BIT             NOT NULL,
    [LastModified]      DATETIME        DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT [Pk_Servers]     PRIMARY KEY CLUSTERED       ([Id]           ASC),
    CONSTRAINT [Unq_Servers]    UNIQUE      NONCLUSTERED    ([IPAddress]    ASC,    [Port]  ASC)
);

-- Additional indexes for better performance
CREATE NONCLUSTERED INDEX [IX_Servers_Active] ON [dbo].[Servers] ([Active] ASC);
CREATE NONCLUSTERED INDEX [IX_Servers_LastModified] ON [dbo].[Servers] ([LastModified] ASC);
