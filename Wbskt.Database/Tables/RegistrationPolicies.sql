/*
    Table: dbo.RegistrationPolicies
    Purpose: Stores registration policy definitions for users, including type, limits, and status.
    Columns:
        - Id: INT, primary key
        - UserId: INT, foreign key to Users
        - RefId: UNIQUEIDENTIFIER, unique policy reference
        - Name: VARCHAR(100), policy name
        - PolicyType: INT, type of policy
        - MaxClients: INT, max clients allowed (nullable)
        - Expiry: DATETIME, expiry date (nullable)
        - LastModified: DATETIME, last modification timestamp
    Constraints: PK, unique, FK to Users, checks for type, usage, and limits
    Author: Richard Joy
    Date: 2025-04-25
    Last Modified: 2025-04-25 by Richard Joy - Initial version
*/
CREATE TABLE [dbo].[RegistrationPolicies] (
    [Id]                INT                 IDENTITY (1, 1) NOT NULL,
    [UserId]            INT                 NOT NULL,
    [RefId]             UNIQUEIDENTIFIER    NOT NULL,
    [Name]              VARCHAR (100)       NOT NULL,
    [MaxClients]        INT                 NULL,     -- For NumberOfClients and TimeAndCount policies
    [Expiry]            DATETIME            NULL,     -- For TimeLimited and TimeAndCount policies
    [Pin]               VARCHAR(6)          NOT NULL,
    [LastModified]      DATETIME2           NOT NULL DEFAULT GETUTCDATE(),
    CONSTRAINT [Pk_RegistrationPolicies]     PRIMARY KEY CLUSTERED       ([Id]   ASC),
    CONSTRAINT [Unq_RegistrationPolicies_RefId] UNIQUE      NONCLUSTERED    ([RefId] ASC),
    CONSTRAINT [Unq_RegistrationPolicies_Pin] UNIQUE      NONCLUSTERED    ([Pin] ASC),
    CONSTRAINT [Unq_RegistrationPolicies_UserId_Name] UNIQUE NONCLUSTERED ([UserId] ASC, [Name] ASC),
    CONSTRAINT [Fk_RegistrationPolicies_Users] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users] ([Id]),
);
GO
