CREATE TABLE dbo.Clients (
    Id             INT              IDENTITY(1, 1) NOT NULL,
    RefId          UNIQUEIDENTIFIER NOT NULL           DEFAULT NEWID(),
    WorkspaceId    INT              NOT NULL,
    PolicyId       INT              NOT NULL,
    Name           NVARCHAR(100)    NOT NULL,
    SecretHash     VARBINARY(32)    NOT NULL, -- SHA-256 of the secret; the plaintext is shown once, at registration, and never stored
    Status         TINYINT          NOT NULL           DEFAULT 0, -- 0: Pending, 1: Registered, 2: Revoked
    IsConnected    BIT              NOT NULL           DEFAULT 0,
    ConnectedAt    DATETIME2(3)     NULL, -- set on connect, cleared on disconnect; uptime = now - ConnectedAt
    LastActivityAt DATETIME2(3)     NULL,
    LastRttMs      INT              NULL, -- last measured socket round-trip
    RttMeasuredAt  DATETIME2(3)     NULL,
    ConnectedHostId NVARCHAR(100)   NULL, -- socket-host instance currently holding this connection
    CreatedAt      DATETIME2(3)     NOT NULL           DEFAULT SYSUTCDATETIME(),

    -- Constraints
    CONSTRAINT PK_Clients                     PRIMARY KEY (Id),
    CONSTRAINT UQ_Clients_RefId               UNIQUE (RefId),
    CONSTRAINT FK_Clients_RegistrationPolicies FOREIGN KEY (PolicyId)
        REFERENCES dbo.RegistrationPolicies (Id)
);
GO

-- Indexes
CREATE INDEX IX_Clients_RefId
    ON dbo.Clients (RefId);
GO
CREATE INDEX IX_Clients_PolicyId
    ON dbo.Clients (PolicyId);
GO
CREATE INDEX IX_Clients_WorkspaceId
    ON dbo.Clients (WorkspaceId); -- Recommended for multi-tenant querying
GO