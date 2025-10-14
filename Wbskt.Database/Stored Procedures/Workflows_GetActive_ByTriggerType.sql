CREATE PROCEDURE [dbo].[Workflows_GetActive_ByTriggerType]
    @TriggerType VARCHAR(50)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT [Id], [RefId], [UserId], [Name], [Description], [IsEnabled], [TriggerType], [TriggerConfiguration], [LastModified]
    FROM [dbo].[Workflows]
    WHERE [IsEnabled] = 1 AND [TriggerType] = @TriggerType;
END
