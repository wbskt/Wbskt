-- Clients deleted with readings still to clear. dbo.Client_Delete queues the client here rather than
-- deleting its history in its own transaction; dbo.ClientReadings_Compact clears its raw and hourly
-- readings in batches and then drops the row.
CREATE TABLE dbo.DeletedClientReadings (
    ClientId  INT          NOT NULL,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_DeletedClientReadings_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_DeletedClientReadings PRIMARY KEY CLUSTERED (ClientId)
);
GO
