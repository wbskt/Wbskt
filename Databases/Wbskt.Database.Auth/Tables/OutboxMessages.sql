-- Events written in the same transaction as the change they describe, and published to the bus by the
-- auth host's OutboxDispatcher afterwards. A row is deleted once the broker has it, so the table holds
-- only what has not been sent yet: a crash or a broker outage between commit and publish delays an
-- event instead of losing it.
CREATE TABLE dbo.OutboxMessages (
    Id BIGINT IDENTITY(1,1) NOT NULL,
    MessageType NVARCHAR(256) NOT NULL,
    Payload NVARCHAR(MAX) NOT NULL,
    CreatedAt DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_OutboxMessages PRIMARY KEY CLUSTERED (Id ASC)
);
GO
