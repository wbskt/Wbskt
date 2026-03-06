SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE VIEW dbo.ViewEvents
AS
SELECT 
    E.EventName, 
    E.EventCriticality,
    EL.CreatedAt, 
    EL.WorkspaceId, 
    EL.EventData 
FROM EventLogs EL 
    INNER JOIN dbo.Events 
        E on E.Id = EL.EventId
    GO
