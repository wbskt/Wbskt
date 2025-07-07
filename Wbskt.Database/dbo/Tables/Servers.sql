/*
    Table: dbo.Servers
    Purpose: Stores server information, including network and status details.
    Columns:
        - Id: INT, primary key
        - IPAddress: VARCHAR(100), server IP address
        - PublicDomainName: VARCHAR(100), public domain name
        - Port: INT, network port
        - Type: INT, server type
        - Active: BIT, server status
        - LastModified: DATETIME, last modification timestamp
    Constraints: PK, unique on IPAddress+Port
    Author: Richard Joy
    Date: 2025-04-25
    Last Modified: 2025-04-25 by Richard Joy - Initial version
*/
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
GO

-- Additional indexes for better performance
CREATE NONCLUSTERED INDEX [IX_Servers_Active] ON [dbo].[Servers] ([Active] ASC);
GO

CREATE NONCLUSTERED INDEX [IX_Servers_LastModified] ON [dbo].[Servers] ([LastModified] ASC);
