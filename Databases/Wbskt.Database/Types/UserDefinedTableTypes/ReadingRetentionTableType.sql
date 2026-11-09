-- How long each plan keeps readings, from the plan catalogue (PlanCatalog): raw, and rolled up per hour.
CREATE TYPE dbo.ReadingRetentionTableType AS TABLE
(
    PlanCode   NVARCHAR(20) NOT NULL PRIMARY KEY,
    RawDays    INT          NOT NULL,
    HourlyDays INT          NOT NULL
);
GO
