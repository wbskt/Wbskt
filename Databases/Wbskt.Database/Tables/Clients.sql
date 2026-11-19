CREATE TABLE dbo.Clients (
    Id             INT              IDENTITY(1, 1) NOT NULL,
    RefId          UNIQUEIDENTIFIER NOT NULL           DEFAULT NEWID(),
    WorkspaceId    INT              NOT NULL,
    PolicyId       INT              NOT NULL,
    Name           NVARCHAR(100)    NOT NULL,
    SecretHash     VARBINARY(32)    NOT NULL, -- SHA-256 of the secret; the plaintext is shown once, at registration, and never stored
    Status         TINYINT          NOT NULL           DEFAULT 0, -- 0: Pending, 1: Registered, 2: Revoked, 3: Rejected (ClientStatus)
    IsConnected    BIT              NOT NULL           DEFAULT 0,
    ConnectedAt    DATETIME2(3)     NULL, -- set on connect, cleared on disconnect; uptime = now - ConnectedAt
    LastActivityAt DATETIME2(3)     NULL,
    LastRttMs      INT              NULL, -- last measured socket round-trip
    RttMeasuredAt  DATETIME2(3)     NULL,
    ConnectedHostId NVARCHAR(100)   NULL, -- socket-host instance currently holding this connection
    UpdatingUntil  DATETIME2(3)     NULL, -- closed to install a firmware update: updating, not offline, until then or its next connect
    CreatedAt      DATETIME2(3)     NOT NULL           DEFAULT SYSUTCDATETIME(),

    -- Constraints
    CONSTRAINT PK_Clients                     PRIMARY KEY (Id),
    CONSTRAINT UQ_Clients_RefId               UNIQUE (RefId),
    CONSTRAINT FK_Clients_RegistrationPolicies FOREIGN KEY (PolicyId)
        REFERENCES dbo.RegistrationPolicies (Id),
    CONSTRAINT CK_Clients_Status CHECK (Status IN (0, 1, 2, 3))
);
GO

-- Indexes
-- Status second: a policy's registered-client count (Client_Create, Client_UpdateStatuses) reads only
-- the key.
CREATE INDEX IX_Clients_PolicyId
    ON dbo.Clients (PolicyId, Status);
GO
-- Status second for the plan's registered-client count pooled across a tenant's workspaces
-- (Client_GetCountBy_Account, WorkspaceUsage_GetSummary).
CREATE INDEX IX_Clients_WorkspaceId
    ON dbo.Clients (WorkspaceId, Status);
GO
-- The presence sweeps (Client_ExpirePresence every minute, Client_ReconcilePresenceBy_HostId for every
-- host snapshot) touch only connected clients; without this each scanned and update-locked every row.
CREATE INDEX IX_Clients_Connected
    ON dbo.Clients (ConnectedHostId)
    INCLUDE (ConnectedAt, RefId, WorkspaceId)
    WHERE IsConnected = 1;
GO
