CREATE PROCEDURE dbo.Client_GetCountBy_PolicyId
    @PolicyId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT COUNT(Id)
    FROM dbo.Clients
    WHERE PolicyId = @PolicyId
      AND Status = 1; -- 1: Registered
END
GO
