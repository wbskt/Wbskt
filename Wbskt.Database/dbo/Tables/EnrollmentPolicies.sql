CREATE TABLE [dbo].[EnrollmentPolicies] (
    [Id]                INT                 IDENTITY (1, 1) NOT NULL,
    [UserId]            INT                 NOT NULL,
    [PolicyRef]         UNIQUEIDENTIFIER    NOT NULL,
    [Name]              VARCHAR (100)       NOT NULL,
    [PolicyType]        INT                 NOT NULL, -- 1: TimeLimited, 2: NumberOfClients, 3: SingleUse
    [MaxClients]        INT                 NULL,     -- For NumberOfClients and SingleUse policies
    [ExpiryDate]        DATETIME            NULL,     -- For TimeLimited policies
    [CurrentUsage]      INT                 NOT NULL DEFAULT 0,
    [IsActive]          BIT                 NOT NULL DEFAULT 1,
    [LastModified]      DATETIME            DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT [Pk_EnrollmentPolicies]     PRIMARY KEY CLUSTERED       ([Id]   ASC),
    CONSTRAINT [Unq_EnrollmentPolicies_PolicyRef] UNIQUE      NONCLUSTERED    ([PolicyRef] ASC),
    CONSTRAINT [Unq_EnrollmentPolicies_UserId_Name] UNIQUE NONCLUSTERED ([UserId] ASC, [Name] ASC),
    CONSTRAINT [Fk_EnrollmentPolicies_Users] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users] ([Id]),
    CONSTRAINT [Chk_EnrollmentPolicies_PolicyType] CHECK ([PolicyType] IN (1, 2, 3)),
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