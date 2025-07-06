CREATE PROCEDURE dbo.Clients_GetAll_UserId_LastModified
    @UserId INT,
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
    WHERE UserId = @UserId
      AND LastModified >= @LastModified
    ORDER BY Id;
END; 