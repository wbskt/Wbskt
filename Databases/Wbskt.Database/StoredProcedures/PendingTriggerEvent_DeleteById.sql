CREATE PROCEDURE dbo.PendingTriggerEvent_DeleteById
    @Id BIGINT
AS
BEGIN
    SET NOCOUNT ON;

    DELETE FROM dbo.PendingTriggerEvents
    WHERE Id = @Id;
END;
GO
