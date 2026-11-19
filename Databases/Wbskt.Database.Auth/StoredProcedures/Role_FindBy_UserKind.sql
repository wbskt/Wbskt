-- The tenant's built-in User role (Kind 'User'), which an invitation that names no role grants on
-- acceptance, and which naming in an invitation needs only users.manage.
CREATE PROCEDURE dbo.Role_FindBy_UserKind
    @TenantId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT Id
    FROM dbo.Roles
    WHERE TenantId = @TenantId
      AND Kind = N'User';
END
GO
