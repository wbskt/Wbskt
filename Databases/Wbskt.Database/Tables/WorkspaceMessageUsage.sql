-- The client messages each workspace carried, per day (UTC): messages its clients sent
-- (ClientMessageReceivedEvent) and commands delivered to them (ClientCommandDeliveredEvent). A plan's
-- monthly message allowance counts both. Only dbo.ClientTraffic_InsertBatch writes it, in the statement
-- batch that writes the traffic rows and from the rows it actually inserted, so it matches dbo.ClientTraffic
-- and a redelivered event adds nothing. Kept after the traffic itself is swept.
CREATE TABLE dbo.WorkspaceMessageUsage
(
    WorkspaceId INT          NOT NULL,
    Day         DATE         NOT NULL,
    Inbound     BIGINT       NOT NULL,
    Outbound    BIGINT       NOT NULL,
    UpdatedAt   DATETIME2(3) NOT NULL CONSTRAINT DF_WorkspaceMessageUsage_UpdatedAt DEFAULT SYSUTCDATETIME(),
    CreatedAt   DATETIME2(3) NOT NULL CONSTRAINT DF_WorkspaceMessageUsage_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_WorkspaceMessageUsage PRIMARY KEY (WorkspaceId, Day)
);
GO
