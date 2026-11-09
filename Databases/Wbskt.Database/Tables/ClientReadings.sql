-- Numeric history of client state variables: one row per number a client reports in a
-- "state.report" message, so a temperature or a battery level can be charted over time.
-- dbo.ClientStateVariables keeps only the latest value; this keeps every one for the plan's
-- RawReadingsDays, after which dbo.ClientReadings_Compact rolls it into dbo.ClientReadingsHourly.
-- Page compressed: the rows repeat their client and name and sit in key order, which it packs well.
CREATE TABLE dbo.ClientReadings (
    ClientId    INT           NOT NULL,
    Name        NVARCHAR(100) NOT NULL,
    -- When the client says it took the reading (its sentAt), or when it arrived if it did not say.
    SentAt  DATETIME2(3)  NOT NULL,
    ReceivedAt  DATETIME2(3)  NOT NULL,
    NumberValue FLOAT         NOT NULL,
    -- Arrived well after it was taken: the client was offline and sent it from its buffer.
    IsLate      BIT           NOT NULL,

    -- Every read is "this client's variable over a time range", which the key order serves directly.
    -- IGNORE_DUP_KEY makes a redelivered report a no-op instead of a duplicate point (or a failed
    -- batch); two readings of one variable in the same millisecond keep the first.
    CONSTRAINT PK_ClientReadings PRIMARY KEY CLUSTERED (ClientId, Name, SentAt)
        WITH (IGNORE_DUP_KEY = ON, DATA_COMPRESSION = PAGE)
    -- No foreign key on ClientId: deleting a client must not have to delete a year of readings in
    -- its transaction. They are unreachable once the client is gone and age out with retention.
);
GO

-- Drives the retention sweep (dbo.ClientReadings_Compact): each client's readings past its plan's
-- cutoff, found by a seek per client rather than by scanning past every reading a longer plan keeps.
CREATE INDEX IX_ClientReadings_ClientId_ReceivedAt
    ON dbo.ClientReadings (ClientId, ReceivedAt)
    WITH (DATA_COMPRESSION = PAGE);
GO
