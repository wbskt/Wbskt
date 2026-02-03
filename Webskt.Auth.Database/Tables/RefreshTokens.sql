CREATE TABLE [dbo].[RefreshTokens]
(
    [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [UserId] INT NOT NULL,
    [Token] NVARCHAR(255) NOT NULL,
    [Expires] DATETIME2 NOT NULL,
    [Revoked] DATETIME2 NULL,
    [CreatedByIp] NVARCHAR(50) NULL,
    [RevokedByIp] NVARCHAR(50) NULL,
    [ReplacedByToken] NVARCHAR(255) NULL,
    CONSTRAINT [FK_RefreshTokens_User] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users]([Id])
)
