-- How many days each plan keeps a finished run, from the plan catalogue (PlanCatalog.RunHistoryDays).
CREATE TYPE dbo.RunHistoryRetentionTableType AS TABLE
(
    PlanCode NVARCHAR(20) NOT NULL PRIMARY KEY,
    Days     INT          NOT NULL
);
GO
