CREATE PROCEDURE dbo.User_FindBy_Username
    @Username NVARCHAR(50)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT Id
    FROM dbo.Users
    WHERE Username = @Username;
END
GO
