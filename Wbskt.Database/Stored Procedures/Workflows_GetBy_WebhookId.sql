CREATE PROCEDURE [dbo].[Workflows_GetBy_WebhookId]
    @WebhookId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    SELECT [Id], [RefId], [UserId], [Name], [Description], [IsEnabled], [TriggerType], [TriggerConfiguration], [LastModified]
    FROM [dbo].[Workflows]
    WHERE [IsEnabled] = 1
      AND [TriggerType] = 'Webhook'
      AND JSON_VALUE([TriggerConfiguration], '$.webhookId') = CAST(@WebhookId AS VARCHAR(36));
END
