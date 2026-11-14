-- Writes one event to the outbox on its own, for a change whose only effect here is the event: a credit
-- grant staff make, which the main database records when the event arrives. Called inside the caller's
-- transaction (the staff action runner's), so the event is sent only if the change commits.
CREATE PROCEDURE dbo.OutboxMessage_Insert
    @MessageType NVARCHAR(256),
    @Payload     NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.OutboxMessages (MessageType, Payload)
    VALUES (@MessageType, @Payload);
END
GO
