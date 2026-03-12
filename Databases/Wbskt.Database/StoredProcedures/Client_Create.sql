CREATE PROCEDURE dbo.Client_Create
    @WorkspaceId INT,
    @PolicyId INT,
    @Name NVARCHAR(100),
    @Secret NVARCHAR(255),
    @Status TINYINT,
    @Id INT OUTPUT,
    @RefId UNIQUEIDENTIFIER OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.Clients (
        WorkspaceId,
        PolicyId,
        Name,
        Secret,
        Status
    )
    VALUES (
        @WorkspaceId,
        @PolicyId,
        @Name,
        @Secret,
        @Status
    );

    SELECT 
        @Id = Id,
        @RefId = RefId
    FROM dbo.Clients
    WHERE Id = SCOPE_IDENTITY();
END
GO
