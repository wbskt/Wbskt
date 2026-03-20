CREATE TYPE dbo.EventLogTableType AS TABLE
(
    EventId       INT,
    EventData     NVARCHAR(MAX),
    CreatedAtUtc  DATETIME2(3),
    WorkspaceId   INT NULL,
    PolicyId      INT NULL,
    ClientId      INT NULL,
    WorkflowId    INT NULL,
    PolicyRefId   UNIQUEIDENTIFIER NULL,
    ClientRefId   UNIQUEIDENTIFIER NULL,
    WorkflowRefId UNIQUEIDENTIFIER NULL
)
GO
