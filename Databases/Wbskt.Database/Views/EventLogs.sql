SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

CREATE VIEW dbo.ViewEvents
AS
SELECT
    e.EventName,
    e.EventCriticality,
    el.CreatedAt,
    el.WorkspaceId,
    el.EventData
FROM dbo.EventLogs AS el
INNER JOIN dbo.Events AS e
    ON e.Id = el.EventId;
GO