CREATE TABLE dbo.WorkflowDefinitions
(
    Id              INT              NOT NULL IDENTITY(1,1) PRIMARY KEY,
    RefId           UNIQUEIDENTIFIER NOT NULL,
    Version         INT              NOT NULL,
    WorkspaceId     INT              NOT NULL,
    Name            NVARCHAR(200)    NOT NULL,
    Description     NVARCHAR(2000)   NULL,
    IsEnabled       BIT              NOT NULL DEFAULT 1,
    DefinitionJson  NVARCHAR(MAX)    NOT NULL,
    PublishedBy     INT              NOT NULL,
    CreatedAt       DATETIME2(3)     NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT UQ_WorkflowDefinitions_RefId_Version UNIQUE (RefId, Version)
);
GO

CREATE INDEX IX_WorkflowDefinitions_WorkspaceId_Enabled
    ON dbo.WorkflowDefinitions (WorkspaceId, IsEnabled);
GO
