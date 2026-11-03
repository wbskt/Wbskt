-- Renames an account. At most once per @MinIntervalSeconds, so a name cannot be cycled to pose as
-- someone in a workspace. Setting the name it already has changes nothing and spends no rename.
--
-- 50022: renamed too recently; the message says when the next rename is allowed.
-- A taken name is a unique-key violation on UQ_Users_Username, which the service reports as a conflict.
CREATE PROCEDURE dbo.User_ChangeUsername
    @UserId             INT,
    @Username           NVARCHAR(50),
    @MinIntervalSeconds INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;

    DECLARE @Current NVARCHAR(50), @ChangedAt DATETIME2(3);

    SELECT @Current = Username, @ChangedAt = UsernameChangedAt
    FROM dbo.Users WITH (UPDLOCK, HOLDLOCK)
    WHERE Id = @UserId;

    -- Binary, so a change of case alone counts as a rename.
    IF @Current COLLATE Latin1_General_BIN2 = @Username COLLATE Latin1_General_BIN2
    BEGIN
        COMMIT TRANSACTION;
        RETURN;
    END

    IF @ChangedAt IS NOT NULL AND DATEADD(SECOND, @MinIntervalSeconds, @ChangedAt) > SYSUTCDATETIME()
    BEGIN
        ROLLBACK TRANSACTION;
        DECLARE @Message NVARCHAR(200) = N'The username was changed recently. It can be changed again after '
            + CONVERT(NVARCHAR(30), DATEADD(SECOND, @MinIntervalSeconds, @ChangedAt), 126) + N' UTC.';
        THROW 50022, @Message, 1;
    END

    UPDATE dbo.Users
    SET Username = @Username,
        UsernameChangedAt = SYSUTCDATETIME(),
        UpdatedAt = SYSUTCDATETIME()
    WHERE Id = @UserId;

    COMMIT TRANSACTION;
END
GO
