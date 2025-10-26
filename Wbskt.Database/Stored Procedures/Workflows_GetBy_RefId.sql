CREATE PROCEDURE [dbo].[Workflows_GetBy_RefId]
    @RefId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    SELECT [Id], [RefId], [UserId], [Name], [Description], [IsEnabled], [TriggerType], [TriggerConfiguration], [ViewportX], [ViewportY], [ViewportZoom], [LastModified]
    FROM [dbo].[Workflows]
    WHERE [RefId] = @RefId;
END
