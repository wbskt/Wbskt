CREATE PROCEDURE dbo.Run_FindBy_RefId
    @RefId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP 1
        Id
    FROM dbo.Runs
    WHERE RefId = @RefId;
END;
GO
