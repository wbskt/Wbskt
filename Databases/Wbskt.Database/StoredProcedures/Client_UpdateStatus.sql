CREATE PROCEDURE dbo.Client_UpdateStatus
    @Id INT,
    @Status TINYINT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.Clients
    SET Status = @Status
    WHERE Id = @Id;
END
GO
