-- dbo.WorkspaceMessageUsage by UTC hour, for the Overview's message chart: messages a workspace's clients
-- sent and commands delivered to them. dbo.ClientTraffic_InsertBatch writes both tables from the same
-- counts, so a day here sums to its row there. Kept EventLogging:RetentionDays (the retention sweep),
-- while the daily table outlives it.
CREATE TABLE dbo.WorkspaceMessageUsageHourly
(
    WorkspaceId INT          NOT NULL,
    [Hour]      DATETIME2(0) NOT NULL,
    Inbound     BIGINT       NOT NULL,
    Outbound    BIGINT       NOT NULL,
    CreatedAt   DATETIME2(3) NOT NULL CONSTRAINT DF_WorkspaceMessageUsageHourly_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_WorkspaceMessageUsageHourly PRIMARY KEY (WorkspaceId, [Hour])
);
GO

-- The retention sweep deletes by hour across every workspace.
CREATE NONCLUSTERED INDEX IX_WorkspaceMessageUsageHourly_Hour
    ON dbo.WorkspaceMessageUsageHourly ([Hour]);
GO
