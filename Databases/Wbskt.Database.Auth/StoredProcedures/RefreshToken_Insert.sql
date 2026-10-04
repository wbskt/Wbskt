-- Starts a session: the first refresh token of a sign-in. Rotation (dbo.RefreshToken_Rotate) carries
-- @SessionId and the start time on to every token after this one.
CREATE PROCEDURE dbo.RefreshToken_Insert
    @UserId INT,
    @TokenHash VARBINARY(32),
    @Expires DATETIME2(3),
    @CreatedByIp NVARCHAR(50),
    @SessionId UNIQUEIDENTIFIER,
    @Id INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.RefreshTokens (
        UserId,
        TokenHash,
        Expires,
        CreatedByIp,
        SessionId
    )
    VALUES (
        @UserId,
        @TokenHash,
        @Expires,
        @CreatedByIp,
        @SessionId
    );

    SET @Id = SCOPE_IDENTITY();
END
GO
