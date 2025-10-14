CREATE PROCEDURE [dbo].[Workflows_Update]
    @RefId UNIQUEIDENTIFIER,
    @Name NVARCHAR(100),
    @Description NVARCHAR(500),
    @IsEnabled BIT,
    @TriggerType INT,
    @TriggerConfiguration NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE [dbo].[Workflows]
    SET [Name] = @Name,
        [Description] = @Description,
        [IsEnabled] = @IsEnabled,
        [TriggerType] = @TriggerType,
        [TriggerConfiguration] = @TriggerConfiguration,
        [LastModified] = GETUTCDATE()
    WHERE [RefId] = @RefId;
END
