CREATE PROCEDURE dbo.EventLogs_Insert 
    @EventId INT,
    @EventData NVARCHAR(MAX),
    @CreatedAtUtc DATETIME2(3),
    @WorkspaceId INT,
    @Id INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.EventLogs (EventId, EventData, CreatedAt, WorkspaceId)
    VALUES (@EventId, @EventData, @CreatedAtUtc, @WorkspaceId);

    SET @Id = SCOPE_IDENTITY();
END
GO
