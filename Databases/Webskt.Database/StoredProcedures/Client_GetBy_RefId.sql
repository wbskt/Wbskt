CREATE PROCEDURE dbo.Client_GetBy_RefId
    @RefId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    SELECT 
        Id,
        RefId,
        PolicyId,
        Name,
        Secret,
        Status,
        CreatedAt
    FROM dbo.Clients
    WHERE RefId = @RefId;
END
