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
    WorkflowRefId UNIQUEIDENTIFIER NULL,
    UserId        INT NULL,
    UserRefId     UNIQUEIDENTIFIER NULL,
    MessageId     UNIQUEIDENTIFIER NULL,
    Source        TINYINT NULL,
    ClientAddress NVARCHAR(45) NULL,
    UserAgent     NVARCHAR(256) NULL
)
GO
