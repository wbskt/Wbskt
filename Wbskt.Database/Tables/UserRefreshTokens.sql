/*
    Table: dbo.UserRefreshTokens
    Purpose: Stores user refresh tokens for authentication.
    Columns:
        - Id: INT, primary key
        - UserId: INT, foreign key to Users
        - Token: VARCHAR(256), the refresh token
        - Expires: DATETIME, the expiration date of the token
        - Created: DATETIME, the creation date of the token
        - CreatedByIp: VARCHAR(50), the IP address of the user who created the token
        - Revoked: DATETIME, the date when the token was revoked
        - RevokedByIp: VARCHAR(50), the IP address of the user who revoked the token
        - ReplacedByToken: VARCHAR(256), the token that replaced this token
    Constraints: PK, FK to Users
    Author: Richard Joy
    Date: 2025-10-12
    Last Modified: 2025-10-12 by Richard Joy - Initial version
*/
CREATE TABLE [dbo].[UserRefreshTokens] (
    [Id] INT IDENTITY (1, 1) NOT NULL,
    [UserId] INT NOT NULL,
    [Token] VARCHAR(256) NOT NULL,
    [Expires] DATETIME NOT NULL,
    [Created] DATETIME NOT NULL DEFAULT GETUTCDATE(),
    [CreatedByIp] VARCHAR(50) NOT NULL,
    [Revoked] DATETIME NULL,
    [RevokedByIp] VARCHAR(50) NULL,
    [ReplacedByToken] VARCHAR(256) NULL,
    CONSTRAINT [Pk_UserRefreshTokens] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [Fk_UserRefreshTokens_Users] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users] ([Id])
);
GO
