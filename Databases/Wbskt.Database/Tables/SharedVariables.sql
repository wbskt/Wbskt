CREATE TABLE dbo.SharedVariables
(
    Id            INT              NOT NULL IDENTITY(1,1) PRIMARY KEY,
    WorkflowRefId UNIQUEIDENTIFIER NOT NULL,
    VarName       NVARCHAR(100)    NOT NULL,
    VarType       NVARCHAR(16)     NOT NULL,
    ValueJson     NVARCHAR(MAX)    NOT NULL,
    UpdatedAt     DATETIME2(3)     NOT NULL DEFAULT SYSUTCDATETIME(),
    CreatedAt     DATETIME2(3)     NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT UQ_SharedVariables_WorkflowRefId_VarName UNIQUE (WorkflowRefId, VarName)
);
GO
