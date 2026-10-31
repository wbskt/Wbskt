SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

-- The event log as its readers see it: the audit log and device traffic together, with one id
-- sequence across both. IsDeviceTraffic lets a reader that wants no traffic drop that branch whole
-- (with OPTION (RECOMPILE), or a constant), instead of reading it to filter every row out.
CREATE VIEW dbo.EventLogEntries
AS
SELECT
    el.Id,
    el.EventId,
    el.WorkspaceId,
    el.PolicyId,
    el.PolicyRefId,
    el.ClientId,
    el.ClientRefId,
    el.WorkflowId,
    el.WorkflowRefId,
    el.UserId,
    el.UserRefId,
    el.EventData,
    el.CreatedAt,
    el.MessageId,
    el.Source,
    el.ClientAddress,
    el.UserAgent,
    CAST(0 AS BIT) AS IsDeviceTraffic
FROM dbo.EventLogs AS el
UNION ALL
SELECT
    dt.Id,
    dt.EventId,
    dt.WorkspaceId,
    dt.PolicyId,
    dt.PolicyRefId,
    dt.ClientId,
    dt.ClientRefId,
    dt.WorkflowId,
    dt.WorkflowRefId,
    dt.UserId,
    dt.UserRefId,
    dt.EventData,
    dt.CreatedAt,
    dt.MessageId,
    dt.Source,
    dt.ClientAddress,
    dt.UserAgent,
    CAST(1 AS BIT) AS IsDeviceTraffic
FROM dbo.DeviceTraffic AS dt;
GO
