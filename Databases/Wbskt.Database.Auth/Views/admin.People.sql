-- Every account, without its password hash. Id is the key the other admin views join on.
CREATE VIEW [admin].People
AS
SELECT
    U.Id,
    U.RefId,
    U.Username,
    U.Email,
    U.IsActive,
    U.IsEmailVerified,
    U.FailedLoginCount,
    U.LockedUntil,
    U.LastLoginAt,
    U.PasswordChangedAt,
    U.UsernameChangedAt,
    U.UpdatedAt,
    U.CreatedAt,
    (SELECT COUNT(*) FROM dbo.TenantMembers TM WHERE TM.UserId = U.Id) AS AccountCount,
    (SELECT COUNT(*) FROM dbo.WorkspaceMembers WM WHERE WM.UserId = U.Id) AS WorkspaceCount
FROM dbo.Users U;
GO
