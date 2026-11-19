-- Records a browser that has just signed in to the account, and keeps only the account's newest
-- @MaxPerUser live devices, so a script signing in over and over cannot grow the table without bound.
CREATE PROCEDURE dbo.UserDevice_Create
    @UserId INT,
    @TokenHash VARBINARY(32),
    @ExpiresAt DATETIME2(3),
    @MaxPerUser INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;

    INSERT INTO dbo.UserDevices (UserId, TokenHash, ExpiresAt)
    VALUES (@UserId, @TokenHash, @ExpiresAt);

    DELETE FROM dbo.UserDevices
    WHERE UserId = @UserId
      AND (ExpiresAt <= SYSUTCDATETIME()
           OR Id NOT IN (
               SELECT TOP (@MaxPerUser) Id
               FROM dbo.UserDevices
               WHERE UserId = @UserId
                 AND ExpiresAt > SYSUTCDATETIME()
               ORDER BY Id DESC));

    COMMIT TRANSACTION;
END
GO
