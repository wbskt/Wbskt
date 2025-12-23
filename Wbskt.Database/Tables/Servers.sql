/*
    Table: dbo.Servers
    Purpose: Stores information about the socket servers.
    Columns:
        - Id: INT, primary key
        - PublicDomainName: VARCHAR(256), the public domain name of the server
        - Status: INT, the status of the server (e.g., 1 = active, 0 = inactive)
    Constraints: PK
    Author: Richard Joy
    Date: 2025-10-12
    Last Modified: 2025-10-12 by Richard Joy - Initial version
*/
CREATE TABLE [dbo].[Servers] (
    [Id] INT IDENTITY (1, 1) NOT NULL,
    [PublicDomainName] NVARCHAR(256) NOT NULL,
    [Status] INT NOT NULL,
    [LastModified] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    CONSTRAINT [Pk_Servers] PRIMARY KEY CLUSTERED ([Id] ASC)
);
GO
