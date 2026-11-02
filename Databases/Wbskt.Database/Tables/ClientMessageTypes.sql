-- The message types each client has sent, kept up to date as messages arrive, so a type picker can
-- list them without scanning dbo.EventLogs. Counts start when the client first sent the type after
-- this table existed.
CREATE TABLE dbo.ClientMessageTypes (
    ClientId      INT            NOT NULL,
    Type          NVARCHAR(100)  NOT NULL,
    MessageCount  BIGINT         NOT NULL,
    FirstSeenAt   DATETIME2(3)   NOT NULL,
    LastSeenAt    DATETIME2(3)   NOT NULL,
    SamplePayload NVARCHAR(4000) NULL, -- the latest payload that fitted, as the device sent it

    -- Constraints
    CONSTRAINT PK_ClientMessageTypes         PRIMARY KEY (ClientId, Type),
    CONSTRAINT FK_ClientMessageTypes_Clients FOREIGN KEY (ClientId)
        REFERENCES dbo.Clients (Id)
);
GO
