-- The trigger registrations a version gets when it becomes live, as ITriggerPlanner planned them.
-- Workflow_Enable writes them under the workflow's lock, in the same transaction as the switch.
CREATE TYPE dbo.TriggerPlanRegistrationTableType AS TABLE
(
    TriggerNodeId         UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    TriggerKind           NVARCHAR(64)     NOT NULL,
    TriggerKey            NVARCHAR(400)    NOT NULL,
    CorrelationExpression NVARCHAR(1000)   NULL,
    ConcurrencyPolicy     NVARCHAR(32)     NOT NULL,
    FilterExpression      NVARCHAR(2000)   NULL,
    WebhookSecret         NVARCHAR(200)    NULL
);
GO
