-- The months in which a workspace's account was found past a share of its plan's message allowance, so
-- each notice is raised once per workspace and month however many management replicas check, and
-- however often (dbo.WorkspaceMessageLimit_Record). Percent is the share: 80 (warned), 100 (reached) or
-- the paid plans' throttle (Billing:PaidMessageThrottleMultiple, as a percentage).
CREATE TABLE dbo.WorkspaceMessageLimitNotices
(
    WorkspaceId INT          NOT NULL,
    MonthStart  DATE         NOT NULL,
    [Percent]   INT          NOT NULL CONSTRAINT DF_WorkspaceMessageLimitNotices_Percent DEFAULT 100,
    CreatedAt   DATETIME2(3) NOT NULL CONSTRAINT DF_WorkspaceMessageLimitNotices_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_WorkspaceMessageLimitNotices PRIMARY KEY (WorkspaceId, MonthStart, [Percent])
);
GO
