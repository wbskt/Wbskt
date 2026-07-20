-- 1:1 sidecar for dbo.Clients holding self-reported SDK metadata and command schemas.
-- Kept off the hot Clients row so list queries never scan the NVARCHAR(MAX) blob.
CREATE TABLE dbo.ClientCapabilities (
    ClientId         INT           NOT NULL,
    AgentName        NVARCHAR(100) NOT NULL, -- ClientCapabilities.Agent  ("csharp-sdk")
    AgentVersion     NVARCHAR(50)  NOT NULL, -- ClientCapabilities.Version
    Platform         NVARCHAR(100) NOT NULL, -- ClientCapabilities.OS     ("linux-arm64")
    CapabilitiesJson NVARCHAR(MAX) NOT NULL, -- serialized list of command capabilities
    UpdatedAt        DATETIME2(3)  NOT NULL  DEFAULT SYSUTCDATETIME(),

    -- Constraints
    CONSTRAINT PK_ClientCapabilities         PRIMARY KEY (ClientId),
    CONSTRAINT FK_ClientCapabilities_Clients FOREIGN KEY (ClientId)
        REFERENCES dbo.Clients (Id)
);
GO
