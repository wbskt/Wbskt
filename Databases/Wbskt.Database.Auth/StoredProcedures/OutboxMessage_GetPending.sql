-- The oldest unsent events after @AfterId, in the order they were written. The dispatcher passes the
-- last id it saw, so rows it cannot read and leaves in place never fill the next batch.
CREATE PROCEDURE dbo.OutboxMessage_GetPending
    @Take INT,
    @AfterId BIGINT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP (@Take)
        Id,
        MessageType,
        Payload,
        CreatedAt
    FROM dbo.OutboxMessages
    WHERE @AfterId IS NULL
       OR Id > @AfterId
    ORDER BY Id;
END
GO
