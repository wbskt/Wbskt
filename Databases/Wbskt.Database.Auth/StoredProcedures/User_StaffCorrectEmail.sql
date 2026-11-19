-- Corrects a mistyped address for staff, on an account that never verified it: the account takes the new
-- address, still unverified, every outstanding verification link and pending address change for the old
-- one is spent, and a fresh verification token is issued for the new one. A verified address is never
-- changed here: that needs its owner, through EmailChange_Confirm. One transaction.
--
-- 50046: the account's address is verified by now.
-- 50047: the new address belongs to another account, or another account's live undo holds it.
-- UQ_Users_Email is the backstop for the second.
CREATE PROCEDURE dbo.User_StaffCorrectEmail
    @Id        INT,
    @Email     NVARCHAR(100),
    @TokenHash VARBINARY(32),
    @ExpiresAt DATETIME2(3)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;

    IF EXISTS (SELECT 1 FROM dbo.Users WITH (UPDLOCK, HOLDLOCK) WHERE Id = @Id AND IsEmailVerified = 1)
    BEGIN
        ROLLBACK TRANSACTION;
        THROW 50046, 'The address is verified.', 1;
    END

    IF EXISTS (SELECT 1 FROM dbo.Users WITH (UPDLOCK, HOLDLOCK) WHERE Email = @Email AND Id <> @Id)
       OR EXISTS (
           SELECT 1
           FROM dbo.EmailChangeTokens WITH (UPDLOCK, HOLDLOCK)
           WHERE OldEmail = @Email
             AND UserId <> @Id
             AND Kind = 2
             AND ConsumedAt IS NULL
             AND ExpiresAt > SYSUTCDATETIME())
    BEGIN
        ROLLBACK TRANSACTION;
        THROW 50047, 'That address cannot be used.', 1;
    END

    UPDATE dbo.Users
    SET Email = @Email,
        IsEmailVerified = 0,
        UpdatedAt = SYSUTCDATETIME()
    WHERE Id = @Id;

    UPDATE dbo.EmailVerificationTokens
    SET ConsumedAt = SYSUTCDATETIME()
    WHERE UserId = @Id
      AND ConsumedAt IS NULL;

    UPDATE dbo.EmailChangeTokens
    SET ConsumedAt = SYSUTCDATETIME()
    WHERE UserId = @Id
      AND Kind = 1
      AND ConsumedAt IS NULL;

    INSERT INTO dbo.EmailVerificationTokens (UserId, TokenHash, ExpiresAt)
    VALUES (@Id, @TokenHash, @ExpiresAt);

    COMMIT TRANSACTION;
END
GO
