CREATE PROCEDURE dbo.ClientStateVariable_GetBy_ClientId
    @ClientId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        Id,
        ClientId,
        Name,
        DataType,
        ValueJson,
        UpdatedAt
    FROM dbo.ClientStateVariables
    WHERE ClientId = @ClientId
    ORDER BY Name;
END
GO
