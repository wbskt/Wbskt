-- Which steps of which published version send through which integration: one row per step per
-- version, written by WorkflowDefinition_Publish in the same transaction as the version. What an
-- integration is "used by" is these rows joined to each live workflow's current version (see
-- dbo.IntegrationUsages). No foreign key to dbo.Integrations: deleting an integration removes its
-- rows here itself, under the lock that refused the delete while a published version used it.
CREATE TABLE dbo.WorkflowIntegrationRefs (
    WorkflowDefinitionId INT              NOT NULL,
    NodeId               UNIQUEIDENTIFIER NOT NULL,
    NodeName             NVARCHAR(200)    NOT NULL,
    IntegrationId        INT              NOT NULL,

    CONSTRAINT PK_WorkflowIntegrationRefs PRIMARY KEY (WorkflowDefinitionId, NodeId)
);
GO

CREATE INDEX IX_WorkflowIntegrationRefs_IntegrationId
    ON dbo.WorkflowIntegrationRefs (IntegrationId) INCLUDE (NodeId, NodeName);
GO
