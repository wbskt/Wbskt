-- dbo.WorkspaceMessageUsageHourly per client, for the client page's message chart: messages the client
-- sent and commands delivered to it, by UTC hour. dbo.ClientTraffic_InsertBatch writes it from the same
-- counts as the workspace tables, so a client's hours sum into its workspace's. Kept
-- EventLogging:RetentionDays (the retention sweep); a day is the sum of its hours.
CREATE TABLE dbo.ClientMessageUsageHourly
(
    ClientId    INT          NOT NULL,
    [Hour]      DATETIME2(0) NOT NULL,
    WorkspaceId INT          NOT NULL,
    Inbound     BIGINT       NOT NULL,
    Outbound    BIGINT       NOT NULL,
    CreatedAt   DATETIME2(3) NOT NULL CONSTRAINT DF_ClientMessageUsageHourly_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_ClientMessageUsageHourly PRIMARY KEY (ClientId, [Hour])
);
GO

-- The retention sweep deletes by hour across every client.
CREATE NONCLUSTERED INDEX IX_ClientMessageUsageHourly_Hour
    ON dbo.ClientMessageUsageHourly ([Hour]);
GO
