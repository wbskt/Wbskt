CREATE PROCEDURE dbo.RefreshToken_Insert
    @UserId INT,
    @Token NVARCHAR(255),
    @Expires DATETIME2(3),
    @CreatedByIp NVARCHAR(50),
    @Id INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    
    INSERT INTO dbo.RefreshTokens (
        UserId, 
        Token, 
        Expires, 
        CreatedByIp
    )
    VALUES (
        @UserId, 
        @Token, 
        @Expires, 
        @CreatedByIp
    );
    
    SET @Id = SCOPE_IDENTITY();
END
GO
