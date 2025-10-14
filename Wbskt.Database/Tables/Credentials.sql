CREATE TABLE [dbo].[Credentials] (
    [Id] INT IDENTITY(1,1) NOT NULL,
    [UserId] INT NOT NULL,
    [IntegrationType] VARCHAR(50) NOT NULL,
    [Name] NVARCHAR(100) NOT NULL,
    [EncryptedCredentials] NVARCHAR(MAX) NOT NULL,
    [LastModified] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    CONSTRAINT [PK_Credentials] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_Credentials_Users] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users]([Id]) ON DELETE CASCADE,
    CONSTRAINT [UNQ_Credentials_UserId_Name] UNIQUE NONCLUSTERED ([UserId], [Name])
);
