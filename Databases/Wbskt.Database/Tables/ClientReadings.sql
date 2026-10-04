-- Numeric history of client state variables: one row per number a device reports in a
-- "state.report" message, so a temperature or a battery level can be charted over time.
-- dbo.ClientStateVariables keeps only the latest value; this keeps every one, for
-- Readings:RetentionDays (dbo.ClientReadings_DeleteBefore).
CREATE TABLE dbo.ClientReadings (
    ClientId    INT           NOT NULL,
    Name        NVARCHAR(100) NOT NULL,
    -- When the device says it took the reading (its sentAt), or when it arrived if it did not say.
    DeviceTime  DATETIME2(3)  NOT NULL,
    ReceivedAt  DATETIME2(3)  NOT NULL,
    NumberValue FLOAT         NOT NULL,
    -- Arrived well after it was taken: the device was offline and sent it from its buffer.
    IsLate      BIT           NOT NULL,

    -- Every read is "this client's variable over a time range", which the key order serves directly.
    -- IGNORE_DUP_KEY makes a redelivered report a no-op instead of a duplicate point (or a failed
    -- batch); two readings of one variable in the same millisecond keep the first.
    CONSTRAINT PK_ClientReadings PRIMARY KEY CLUSTERED (ClientId, Name, DeviceTime)
        WITH (IGNORE_DUP_KEY = ON)
    -- No foreign key on ClientId: deleting a client must not have to delete a year of readings in
    -- its transaction. They are unreachable once the client is gone and age out with retention.
);
GO

-- Drives the retention sweep (dbo.ClientReadings_DeleteBefore).
CREATE INDEX IX_ClientReadings_ReceivedAt
    ON dbo.ClientReadings (ReceivedAt);
GO
