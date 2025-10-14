CREATE PROCEDURE [dbo].[Workflows_GetAll_ByUserId]
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT [Id], [RefId], [UserId], [Name], [Description], [IsEnabled], [TriggerType], [TriggerConfiguration], [LastModified]
    FROM [dbo].[Workflows]
    WHERE [UserId] = @UserId;
END
