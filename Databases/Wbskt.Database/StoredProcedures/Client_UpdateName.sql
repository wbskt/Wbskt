CREATE PROCEDURE dbo.Client_UpdateName
    @Id INT,
    @Name NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.Clients
    SET Name = @Name
    WHERE Id = @Id;
END
GO
