-- 50027: the address is held by another account's live email-change undo. Moving an account off an
-- address frees it in UQ_Users_Email, so without this a sign-up on the old address, verified or not,
-- would make that undo fail and leave the owner no way back.
CREATE PROCEDURE dbo.User_Create
    @Username NVARCHAR(50),
    @Email NVARCHAR(100),
    @PasswordHash NVARCHAR(255),
    @Id INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;

    IF EXISTS (
        SELECT 1
        FROM dbo.EmailChangeTokens WITH (UPDLOCK, HOLDLOCK)
        WHERE OldEmail = @Email
          AND Kind = 2
          AND ConsumedAt IS NULL
          AND ExpiresAt > SYSUTCDATETIME())
    BEGIN
        ROLLBACK TRANSACTION;
        THROW 50027, 'That address is reserved.', 1;
    END

    INSERT INTO dbo.Users (
        Username,
        Email,
        PasswordHash
    )
    VALUES (
        @Username,
        @Email,
        @PasswordHash
    );

    SET @Id = SCOPE_IDENTITY();

    COMMIT TRANSACTION;
END
GO
