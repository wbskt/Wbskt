CREATE PROCEDURE dbo.EventLogs_Insert 
    @EventId INT,
    @EventData NVARCHAR(MAX),
    @CreatedAtUtc DATETIME2(0),
    @Id INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.EventLogs (EventId, EventData, CreatedAt)
    VALUES (@EventId, @EventData, @CreatedAtUtc);

    SET @Id = SCOPE_IDENTITY();
END
GO
