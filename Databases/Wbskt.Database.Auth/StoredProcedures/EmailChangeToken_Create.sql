-- Starts a change of address: a confirm token for @NewEmail, replacing any change still pending for
-- the account. The old address is recorded now, so the link stops working if the address moves
-- again before it is followed.
CREATE PROCEDURE dbo.EmailChangeToken_Create
    @UserId        INT,
    @NewEmail      NVARCHAR(100),
    @TokenHash     VARBINARY(32),
    @ExpiresAt     DATETIME2(3),
    @RequestedByIp NVARCHAR(50) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;

    UPDATE dbo.EmailChangeTokens
    SET ConsumedAt = SYSUTCDATETIME()
    WHERE UserId = @UserId
      AND Kind = 1
      AND ConsumedAt IS NULL;

    INSERT INTO dbo.EmailChangeTokens (UserId, Kind, TokenHash, OldEmail, NewEmail, ExpiresAt, RequestedByIp)
    SELECT Id, 1, @TokenHash, Email, @NewEmail, @ExpiresAt, @RequestedByIp
    FROM dbo.Users
    WHERE Id = @UserId;

    COMMIT TRANSACTION;
END
GO
