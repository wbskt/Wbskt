-- Outstanding invitations for a tenant: what an administrator needs to see who has been invited but
-- has not joined. Accepted and revoked rows are excluded -- a member who has joined shows up under
-- members, and a withdrawn invitation is not pending work.
--
-- TokenHash is never selected. The raw token was returned once at creation; this list exists to
-- manage invitations, not to recover them.
CREATE PROCEDURE dbo.TenantInvitation_GetAll
    @TenantId INT,
    @Skip INT,
    @Take INT,
    @TotalCount INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT @TotalCount = COUNT(1)
    FROM dbo.TenantInvitations
    WHERE TenantId = @TenantId
      AND AcceptedAt IS NULL
      AND RevokedAt IS NULL;

    SELECT I.RefId,
           I.Email,
           R.RefId AS RoleRefId,
           R.Name AS RoleName,
           I.ExpiresAt,
           I.CreatedAt
    FROM dbo.TenantInvitations I
    LEFT JOIN dbo.Roles R ON R.Id = I.RoleId
    WHERE I.TenantId = @TenantId
      AND I.AcceptedAt IS NULL
      AND I.RevokedAt IS NULL
    ORDER BY I.CreatedAt DESC
    OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
END
GO
