CREATE PROCEDURE [dbo].[Workflows_Insert]
    @RefId UNIQUEIDENTIFIER,
    @UserId INT,
    @Name NVARCHAR(100),
    @Description NVARCHAR(500),
    @IsEnabled BIT,
    @TriggerType INT,
    @TriggerConfiguration NVARCHAR(MAX),
    @Id INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO [dbo].[Workflows] ([RefId], [UserId], [Name], [Description], [IsEnabled], [TriggerType], [TriggerConfiguration])
    VALUES (@RefId, @UserId, @Name, @Description, @IsEnabled, @TriggerType, @TriggerConfiguration);

    SET @Id = SCOPE_IDENTITY();
END
