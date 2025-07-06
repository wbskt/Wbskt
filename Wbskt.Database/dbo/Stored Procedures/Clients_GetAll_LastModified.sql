CREATE PROCEDURE dbo.Clients_GetAll_LastModified
    @LastModified DATETIME
AS
BEGIN
    SET NOCOUNT ON;
    
    SELECT
        Id,
        Name,
        UserId,
        ServerId,
        UniqueRef,
        LastModified
    FROM dbo.Clients
    WHERE LastModified >= @LastModified
    ORDER BY Id;
END; 