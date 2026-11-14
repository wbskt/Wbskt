-- Turns an account's sign-in on or off for staff. Sign-in and token refresh both refuse an inactive
-- account; turning it off also revokes every live refresh token in the same transaction, so no session
-- outlives it. Unlike dbo.User_SetActive there is no last-administrator guard: staff act on the account
-- across every tenant it belongs to, and a tenant whose only administrator is off keeps its data and
-- gets its administrator back when sign-in is turned on again.
CREATE PROCEDURE dbo.User_StaffSetActive
    @Id INT,
    @IsActive BIT,
    @RevokedByIp NVARCHAR(50)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;

    UPDATE dbo.Users
    SET IsActive = @IsActive
    WHERE Id = @Id;

    IF @IsActive = 0
    BEGIN
        UPDATE dbo.RefreshTokens
        SET Revoked = SYSUTCDATETIME(),
            RevokedByIp = @RevokedByIp
        WHERE UserId = @Id
          AND Revoked IS NULL;
    END

    COMMIT TRANSACTION;
END
GO
