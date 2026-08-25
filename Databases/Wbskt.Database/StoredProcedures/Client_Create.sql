CREATE PROCEDURE dbo.Client_Create
    @WorkspaceId INT,
    @PolicyId INT,
    @Name NVARCHAR(100),
    @SecretHash VARBINARY(32),
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
        SecretHash,
        Status
    )
    VALUES (
        @WorkspaceId,
        @PolicyId,
        @Name,
        @SecretHash,
        @Status
    );

    SELECT 
        @Id = Id,
        @RefId = RefId
    FROM dbo.Clients
    WHERE Id = SCOPE_IDENTITY();
END
GO
