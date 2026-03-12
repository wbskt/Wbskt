CREATE PROCEDURE dbo.Client_FindBy_RefId
    @RefId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    SELECT Id
    FROM dbo.Clients
    WHERE RefId = @RefId;
END
GO
