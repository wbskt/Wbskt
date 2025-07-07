/*
    Table: dbo.EnrollmentPolicies
    Purpose: Stores enrollment policy definitions for users, including type, limits, and status.
    Columns:
        - Id: INT, primary key
        - UserId: INT, foreign key to Users
        - PolicyRef: UNIQUEIDENTIFIER, unique policy reference
        - Name: VARCHAR(100), policy name
        - PolicyType: INT, type of policy
        - MaxClients: INT, max clients allowed (nullable)
        - ExpiryDate: DATETIME, expiry date (nullable)
        - CurrentUsage: INT, current usage count
        - IsActive: BIT, active status
        - LastModified: DATETIME, last modification timestamp
    Constraints: PK, unique, FK to Users, checks for type, usage, and limits
    Author: Richard Joy
    Date: 2025-04-25
    Last Modified: 2025-04-25 by Richard Joy - Initial version
*/
CREATE TABLE [dbo].[EnrollmentPolicies] (
    [Id]                INT                 IDENTITY (1, 1) NOT NULL,
    [UserId]            INT                 NOT NULL,
    [PolicyRef]         UNIQUEIDENTIFIER    NOT NULL,
    [Name]              VARCHAR (100)       NOT NULL,
    [PolicyType]        INT                 NOT NULL, -- 1: TimeLimited, 2: NumberOfClients, 3: Unlimited, 4: TimeAndCount
    [MaxClients]        INT                 NULL,     -- For NumberOfClients and TimeAndCount policies
    [ExpiryDate]        DATETIME            NULL,     -- For TimeLimited and TimeAndCount policies
    [CurrentUsage]      INT                 NOT NULL DEFAULT 0,
    [IsActive]          BIT                 NOT NULL DEFAULT 1,
    [LastModified]      DATETIME            DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT [Pk_EnrollmentPolicies]     PRIMARY KEY CLUSTERED       ([Id]   ASC),
    CONSTRAINT [Unq_EnrollmentPolicies_PolicyRef] UNIQUE      NONCLUSTERED    ([PolicyRef] ASC),
    CONSTRAINT [Unq_EnrollmentPolicies_UserId_Name] UNIQUE NONCLUSTERED ([UserId] ASC, [Name] ASC),
    CONSTRAINT [Fk_EnrollmentPolicies_Users] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users] ([Id]),
    CONSTRAINT [Chk_EnrollmentPolicies_PolicyType] CHECK ([PolicyType] IN (1, 2, 3, 4)),
    CONSTRAINT [Chk_EnrollmentPolicies_MaxClients] CHECK ([MaxClients] IS NULL OR [MaxClients] > 0),
    CONSTRAINT [Chk_EnrollmentPolicies_CurrentUsage] CHECK ([CurrentUsage] >= 0)
);
GO

-- Additional indexes for better performance
CREATE NONCLUSTERED INDEX [IX_EnrollmentPolicies_UserId] ON [dbo].[EnrollmentPolicies] ([UserId] ASC);
GO

CREATE NONCLUSTERED INDEX [IX_EnrollmentPolicies_PolicyRef_IsActive] ON [dbo].[EnrollmentPolicies] ([PolicyRef] ASC, [IsActive] ASC);
GO

CREATE NONCLUSTERED INDEX [IX_EnrollmentPolicies_LastModified] ON [dbo].[EnrollmentPolicies] ([LastModified] ASC);
GO 