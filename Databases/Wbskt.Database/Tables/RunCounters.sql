CREATE TABLE dbo.RunCounters
(
    RunId             INT           NOT NULL PRIMARY KEY,
    ActiveBranchCount INT           NOT NULL DEFAULT 0,
    CreditsConsumed   DECIMAL(18,4) NOT NULL DEFAULT 0,
    -- How much of CreditsConsumed is already in dbo.WorkspaceCreditUsage (dbo.RunCredits_Post).
    PostedCredits     DECIMAL(18,4) NOT NULL CONSTRAINT DF_RunCounters_PostedCredits DEFAULT 0,
    UpdatedAt         DATETIME2(3)  NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_RunCounters_Runs FOREIGN KEY (RunId) REFERENCES dbo.Runs (Id)
);
GO
