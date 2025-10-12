/*
    Table: dbo.Clients
    Purpose: Stores client information and their registration status.
    Columns:
        - Id: INT, primary key
        - RefId: UNIQUEIDENTIFIER, unique client reference
        - UserId: INT, foreign key to Users
        - RegistrationPolicyId: INT, foreign key to RegistrationPolicies
        - Name: NVARCHAR(100), client name (nullable)
        - Active: BIT, indicates if the client is active
        - LastModified: DATETIME2, timestamp of the last modification
    Constraints: PK, unique, FK to Users, FK to RegistrationPolicies
    Author: Richard Joy
    Date: 2025-10-12
    Last Modified: 2025-10-12 by Richard Joy - Added LastModified for SqlDependency
*/
CREATE TABLE [dbo].[Clients] (
    [Id]                    INT                 IDENTITY (1, 1) NOT NULL,
    [RefId]                 UNIQUEIDENTIFIER    NOT NULL,
    [UserId]                INT                 NOT NULL,
    [RegistrationPolicyId]  INT                 NOT NULL,
    [Name]                  NVARCHAR (100)      NULL,
    [Active]                BIT                 NOT NULL DEFAULT 1,
    [LastModified]          DATETIME2           NOT NULL DEFAULT GETUTCDATE(),
    CONSTRAINT [Pk_Clients] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [Unq_Clients_RefId] UNIQUE NONCLUSTERED ([RefId] ASC),
    CONSTRAINT [Fk_Clients_Users] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users] ([Id]),
    CONSTRAINT [Fk_Clients_RegistrationPolicies] FOREIGN KEY ([RegistrationPolicyId]) REFERENCES [dbo].[RegistrationPolicies] ([Id])
);
GO

CREATE NONCLUSTERED INDEX [IX_Clients_UserId] ON [dbo].[Clients] ([UserId] ASC);
GO
