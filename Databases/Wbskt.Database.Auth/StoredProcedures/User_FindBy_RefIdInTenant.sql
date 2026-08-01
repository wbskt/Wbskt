-- Resolves a user reference only within a tenant the caller administers, so a user reference from
-- outside the tenant does not resolve and cannot be pulled into its permission graph.
CREATE PROCEDURE dbo.User_FindBy_RefIdInTenant
    @RefId UNIQUEIDENTIFIER,
    @TenantId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT U.Id
    FROM dbo.Users U
    INNER JOIN dbo.TenantMembers TM ON TM.UserId = U.Id
    WHERE U.RefId = @RefId
      AND TM.TenantId = @TenantId;
END
GO
