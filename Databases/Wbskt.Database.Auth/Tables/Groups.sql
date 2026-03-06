CREATE TABLE [dbo].[Groups]
(
    [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [Name] NVARCHAR(100) NOT NULL UNIQUE,
    [ParentGroupId] INT NULL,
    CONSTRAINT [FK_Groups_Parent] FOREIGN KEY ([ParentGroupId]) REFERENCES [dbo].[Groups]([Id])
)
