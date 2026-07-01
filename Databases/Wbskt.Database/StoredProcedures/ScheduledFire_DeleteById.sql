CREATE PROCEDURE dbo.ScheduledFire_DeleteById
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;

    DELETE FROM dbo.ScheduledFires
    WHERE Id = @Id;
END;
GO
