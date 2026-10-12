-- Called once the broker has accepted the event.
CREATE PROCEDURE dbo.OutboxMessage_Delete
    @Id BIGINT
AS
BEGIN
    SET NOCOUNT ON;

    DELETE FROM dbo.OutboxMessages WHERE Id = @Id;
END
GO
