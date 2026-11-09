-- Every webhook a payment provider delivered, stored as received before it is acted on, once per
-- provider and event id (the webhook-id header). A delivery of an event already processed is answered
-- without acting again; one that failed (ProcessedAt still NULL) is processed again on redelivery.
CREATE TABLE dbo.BillingWebhookEvents
(
    Id          BIGINT         IDENTITY(1, 1) NOT NULL,
    Provider    NVARCHAR(40)   NOT NULL,
    EventId     NVARCHAR(200)  NOT NULL,
    EventType   NVARCHAR(100)  NOT NULL,
    Payload     NVARCHAR(MAX)  NOT NULL,
    Attempts    INT            NOT NULL CONSTRAINT DF_BillingWebhookEvents_Attempts DEFAULT 0,
    LastError   NVARCHAR(1000) NULL,
    ProcessedAt DATETIME2(3)   NULL,
    CreatedAt   DATETIME2(3)   NOT NULL CONSTRAINT DF_BillingWebhookEvents_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_BillingWebhookEvents PRIMARY KEY (Id),
    CONSTRAINT UQ_BillingWebhookEvents_Event UNIQUE (Provider, EventId)
);
GO
