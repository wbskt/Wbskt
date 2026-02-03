CREATE PROCEDURE [dbo].[sp_CreateGroup]
    @Name NVARCHAR(100),
    @ParentGroupId INT = NULL
AS
BEGIN
    INSERT INTO [dbo].[Groups] (Name, ParentGroupId)
    VALUES (@Name, @ParentGroupId);
END
