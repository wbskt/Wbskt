-- The months in which a workspace's account was found over its plan's message allowance, so that
-- WorkspaceMessageLimitReachedEvent is raised once per workspace and month however many management
-- replicas check, and however often (dbo.WorkspaceMessageLimit_Record).
CREATE TABLE dbo.WorkspaceMessageLimitNotices
(
    WorkspaceId INT          NOT NULL,
    MonthStart  DATE         NOT NULL,
    CreatedAt   DATETIME2(3) NOT NULL CONSTRAINT DF_WorkspaceMessageLimitNotices_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_WorkspaceMessageLimitNotices PRIMARY KEY (WorkspaceId, MonthStart)
);
GO
