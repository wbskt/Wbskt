-- Readings rolled up per client, variable and hour (by the client's SentAt), once their raw rows pass
-- the plan's RawReadingsDays, and kept for its HourlyReadingsDays. dbo.ClientReadings_Compact moves a
-- raw row here in the same statement that deletes it, so each reading is counted in exactly one of the
-- two tables and a chart reads both (dbo.ClientReadings_GetBuckets). A late reading for an hour already
-- rolled up adds to it: the row keeps a count and a sum, not an average.
CREATE TABLE dbo.ClientReadingsHourly (
    ClientId     INT           NOT NULL,
    Name         NVARCHAR(100) NOT NULL,
    HourStart    DATETIME2(0)  NOT NULL,
    ReadingCount INT           NOT NULL,
    MinValue     FLOAT         NOT NULL,
    MaxValue     FLOAT         NOT NULL,
    SumValue     FLOAT         NOT NULL,
    CreatedAt    DATETIME2(3)  NOT NULL CONSTRAINT DF_ClientReadingsHourly_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_ClientReadingsHourly PRIMARY KEY CLUSTERED (ClientId, Name, HourStart)
        WITH (DATA_COMPRESSION = PAGE)
);
GO

-- The hourly retention sweep: a seek per client for the hours past its plan's cutoff.
CREATE INDEX IX_ClientReadingsHourly_ClientId_HourStart
    ON dbo.ClientReadingsHourly (ClientId, HourStart)
    WITH (DATA_COMPRESSION = PAGE);
GO
