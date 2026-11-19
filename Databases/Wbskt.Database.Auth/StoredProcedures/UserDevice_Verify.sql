-- Whether the browser's sign-in device token is a live one for this account.
CREATE PROCEDURE dbo.UserDevice_Verify
    @UserId INT,
    @TokenHash VARBINARY(32)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT CAST(IIF(EXISTS (
        SELECT 1
        FROM dbo.UserDevices
        WHERE UserId = @UserId
          AND TokenHash = @TokenHash
          AND ExpiresAt > SYSUTCDATETIME()), 1, 0) AS BIT) AS IsKnown;
END
GO
