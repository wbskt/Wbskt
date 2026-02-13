CREATE TABLE dbo.EventLogs (
    Id INT IDENTITY(1, 1) NOT NULL PRIMARY KEY,
    EventId INT NOT NULL,
    EventData NVARCHAR(MAX) NULL, -- JSON payload
    CreatedAt DATETIME2(0) NOT NULL DEFAULT GETUTCDATE(),
    CONSTRAINT FK_EventLogs_Events FOREIGN KEY (EventId) REFERENCES dbo.Events(Id)
);
GO

CREATE INDEX IX_EventLogs_EventId ON dbo.EventLogs(EventId);
GO

CREATE INDEX IX_EventLogs_CreatedAt ON dbo.EventLogs(CreatedAt);
GO
