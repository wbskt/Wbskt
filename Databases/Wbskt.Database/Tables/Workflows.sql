CREATE TABLE dbo.Workflows (
    Id             INT              IDENTITY(1, 1) NOT NULL,
    RefId          UNIQUEIDENTIFIER NOT NULL           DEFAULT NEWID(),
    WorkspaceId    INT              NOT NULL,
    Name           NVARCHAR(100)    NOT NULL,
    Description    NVARCHAR(500)    NOT NULL           DEFAULT '',
    IsEnabled      BIT              NOT NULL           DEFAULT 1,
    Version        INT              NOT NULL           DEFAULT 1,
    DefinitionJson NVARCHAR(MAX)    NOT NULL           DEFAULT '{}',
    CreatedAt      DATETIME2(3)     NOT NULL           DEFAULT SYSUTCDATETIME(),

    -- Constraints
    CONSTRAINT PK_Workflows       PRIMARY KEY (Id),
    CONSTRAINT UQ_Workflows_RefId UNIQUE (RefId)
);
GO

-- Indexes
CREATE INDEX IX_Workflows_RefId
    ON dbo.Workflows (RefId);
GO
CREATE INDEX IX_Workflows_WorkspaceId
    ON dbo.Workflows (WorkspaceId);
GO