-- Replaces a password hash with a stronger one of the same password, after a sign-in proved the
-- password and the hasher reported the stored hash outdated (a lower work factor or older format).
-- Only if the hash is still the one that was verified: a password changed or reset in the meantime
-- must not be overwritten with the old password.
CREATE PROCEDURE dbo.User_UpgradePasswordHash
    @UserId INT,
    @CurrentPasswordHash NVARCHAR(255),
    @NewPasswordHash NVARCHAR(255)
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.Users
    SET PasswordHash = @NewPasswordHash
    WHERE Id = @UserId
      AND PasswordHash = @CurrentPasswordHash;

    SELECT @@ROWCOUNT AS UpdatedCount;
END
GO
