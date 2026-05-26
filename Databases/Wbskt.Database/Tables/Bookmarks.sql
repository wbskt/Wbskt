CREATE TABLE dbo.Bookmarks
(
    Id                INT              NOT NULL IDENTITY(1,1) PRIMARY KEY,
    RefId             UNIQUEIDENTIFIER NOT NULL,
    RunId             INT              NOT NULL,
    BranchRefId       UNIQUEIDENTIFIER NOT NULL,
    NodeId            UNIQUEIDENTIFIER NOT NULL,
    WakeConditionKind NVARCHAR(64)     NOT NULL,
    MatchKey          NVARCHAR(400)    NOT NULL,
    WakeConditionJson NVARCHAR(MAX)    NOT NULL,
    ExpiresAt         DATETIME2(3)     NULL,
    TtlPort           NVARCHAR(128)    NULL,
    CreatedAt         DATETIME2(3)     NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT UQ_Bookmarks_RefId UNIQUE (RefId),
    CONSTRAINT FK_Bookmarks_Runs FOREIGN KEY (RunId) REFERENCES dbo.Runs (Id)
);
GO

CREATE INDEX IX_Bookmarks_MatchKey
    ON dbo.Bookmarks (MatchKey)
    INCLUDE (RunId, BranchRefId, NodeId, WakeConditionKind);
GO

CREATE INDEX IX_Bookmarks_ExpiresAt
    ON dbo.Bookmarks (ExpiresAt)
    WHERE ExpiresAt IS NOT NULL;
GO

CREATE INDEX IX_Bookmarks_RunId
    ON dbo.Bookmarks (RunId);
GO
