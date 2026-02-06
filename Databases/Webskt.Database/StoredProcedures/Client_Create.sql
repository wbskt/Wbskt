CREATE PROCEDURE dbo.Client_Create
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
        PolicyId,
        Name,
        Secret,
        Status
    )
    VALUES (
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
