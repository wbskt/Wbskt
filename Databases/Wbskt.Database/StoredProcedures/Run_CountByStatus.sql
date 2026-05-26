CREATE PROCEDURE dbo.Run_CountByStatus
    @Status NVARCHAR(32)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT COUNT_BIG(*)
    FROM dbo.Runs
    WHERE Status = @Status;
END;
GO
