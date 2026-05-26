CREATE PROCEDURE dbo.PendingTriggerEvent_CountAll
AS
BEGIN
    SET NOCOUNT ON;

    SELECT COUNT_BIG(*)
    FROM dbo.PendingTriggerEvents;
END;
GO
