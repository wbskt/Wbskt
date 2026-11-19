CREATE TABLE dbo.TriggerRegistrations
(
    Id                    INT              NOT NULL IDENTITY(1,1) PRIMARY KEY,
    WorkflowDefinitionId  INT              NOT NULL,
    WorkflowRefId         UNIQUEIDENTIFIER NOT NULL,
    WorkflowVersion       INT              NOT NULL,
    TriggerNodeId         UNIQUEIDENTIFIER NOT NULL,
    TriggerKind           NVARCHAR(64)     NOT NULL,
    TriggerKey            NVARCHAR(400)    NOT NULL,
    CorrelationExpression NVARCHAR(1000)   NULL,
    ConcurrencyPolicy     NVARCHAR(32)     NOT NULL DEFAULT N'Queue',
    FilterExpression      NVARCHAR(2000)   NULL,
    -- Shared secret a webhook caller must present in X-Wbskt-Secret. NULL means the trigger is open,
    -- which is what every registration published before this column existed will read as.
    WebhookSecret         NVARCHAR(200)    NULL,
    -- Lower-case hex SHA-256 of the UTF-8 secret (WebhookSecretHash.Of), which the dispatcher compares
    -- the caller's presented hash with. WebhookSecret is still written for one release, so an engine
    -- rolled back to the previous image does not read a guarded webhook as open; the release after
    -- stops writing it, clears it and drops it (#1059).
    WebhookSecretHash     VARCHAR(64)      NULL,
    CreatedAt             DATETIME2(3)     NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_TriggerRegistrations_WorkflowDefinitions
        FOREIGN KEY (WorkflowDefinitionId) REFERENCES dbo.WorkflowDefinitions (Id)
);
GO

CREATE INDEX IX_TriggerRegistrations_TriggerKey
    ON dbo.TriggerRegistrations (TriggerKey)
    INCLUDE (WorkflowDefinitionId, TriggerNodeId, ConcurrencyPolicy);
GO

CREATE INDEX IX_TriggerRegistrations_WorkflowDefinitionId
    ON dbo.TriggerRegistrations (WorkflowDefinitionId);
GO
