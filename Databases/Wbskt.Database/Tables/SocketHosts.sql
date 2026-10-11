-- When the management host last heard each socket host's heartbeat (SocketHostHeartbeat). Presence
-- held by a host missing from here, or not heard from recently, is expired by Client_ExpirePresence.
CREATE TABLE dbo.SocketHosts (
    HostId     NVARCHAR(100) NOT NULL,
    LastSeenAt DATETIME2(3)  NOT NULL,

    CONSTRAINT PK_SocketHosts PRIMARY KEY (HostId)
);
GO
