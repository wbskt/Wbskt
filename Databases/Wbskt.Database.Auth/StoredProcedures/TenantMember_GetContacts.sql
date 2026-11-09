-- The tenant's members who can be written to: active, not suspended here, with a confirmed address.
-- Which of them hold a permission is dbo.Permission_Verify's to say.
CREATE PROCEDURE dbo.TenantMember_GetContacts
    @TenantId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        u.Id,
        u.Username,
        u.Email
    FROM dbo.TenantMembers tm
    JOIN dbo.Users u ON u.Id = tm.UserId
    WHERE tm.TenantId = @TenantId
      AND tm.IsSuspended = 0
      AND u.IsActive = 1
      AND u.IsEmailVerified = 1
    ORDER BY u.Id;
END
GO
