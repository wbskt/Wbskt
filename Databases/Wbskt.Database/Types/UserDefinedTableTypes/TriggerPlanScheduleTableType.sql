-- The schedules a version gets when it becomes live, each seeded with its next occurrence from now.
CREATE TYPE dbo.TriggerPlanScheduleTableType AS TABLE
(
    TriggerNodeId  UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    CronOrInterval NVARCHAR(200)    NOT NULL,
    NextFireAt     DATETIME2(3)     NOT NULL
);
GO
