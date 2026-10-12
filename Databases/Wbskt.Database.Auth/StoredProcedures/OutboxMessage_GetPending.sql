-- The oldest unsent events, in the order they were written.
CREATE PROCEDURE dbo.OutboxMessage_GetPending
    @Take INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP (@Take)
        Id,
        MessageType,
        Payload,
        CreatedAt
    FROM dbo.OutboxMessages
    ORDER BY Id;
END
GO
