-- Distributed lease rows for active/standby coordination (engine leader election). One row per
-- lease name; TTL-based ownership via ExpiresAt rather than a held connection, since
-- BaseSqlProvider opens a connection per call and can't hold sp_getapplock across ticks.
CREATE TABLE dbo.Leases (
    LeaseName  NVARCHAR(100) NOT NULL,
    HolderId   NVARCHAR(100) NOT NULL,
    ExpiresAt  DATETIME2(3)  NOT NULL,
    AcquiredAt DATETIME2(3)  NOT NULL,

    CONSTRAINT PK_Leases PRIMARY KEY (LeaseName)
);
GO
