CREATE PROCEDURE dbo.Workflow_Update
    @Id INT,
    @Name NVARCHAR(100),
    @Description NVARCHAR(500),
    @IsEnabled BIT,
    @DefinitionJson NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.Workflows
    SET Name = @Name,
        Description = @Description,
        IsEnabled = @IsEnabled,
        DefinitionJson = @DefinitionJson,
        Version = Version + 1
    WHERE Id = @Id;
END
