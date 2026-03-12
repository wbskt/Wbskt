CREATE PROCEDURE dbo.Group_Create
    @Name NVARCHAR(100),
    @ParentGroupId INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

INSERT INTO dbo.Groups (
        Name, 
        ParentGroupId
    )
    VALUES (
        @Name, 
        @ParentGroupId
    );
END
GO
