CREATE PROCEDURE dbo.RefreshToken_Insert
    @UserId INT,
    @TokenHash VARBINARY(32),
    @Expires DATETIME2(3),
    @CreatedByIp NVARCHAR(50),
    @Id INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.RefreshTokens (
        UserId,
        TokenHash,
        Expires,
        CreatedByIp
    )
    VALUES (
        @UserId,
        @TokenHash,
        @Expires,
        @CreatedByIp
    );

    SET @Id = SCOPE_IDENTITY();
END
GO
