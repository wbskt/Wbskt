-- The audit log's rows about a person: sign-ins, failed sign-ins, token rotation, permission
-- changes. Events about a person are logged once per workspace they belong to, so one event can be
-- several rows here with the same MessageId. Read it by UserId (the Id in the auth database's
-- admin.People), which IX_EventLogs_UserId_CreatedAt covers.
CREATE VIEW [admin].PersonEvents
AS
SELECT
    EL.Id,
    EL.UserId,
    EL.UserRefId,
    E.EventName,
    E.EventCriticality,
    EL.WorkspaceId,
    EL.Source,
    EL.ClientAddress,
    EL.UserAgent,
    EL.EventData,
    EL.MessageId,
    EL.CreatedAt
FROM dbo.EventLogs EL
INNER JOIN dbo.Events E ON E.Id = EL.EventId
WHERE EL.UserId IS NOT NULL;
GO
