-- The steps that use an integration, in each live workflow's current version.
CREATE PROCEDURE dbo.Integration_GetUsage
    @IntegrationId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT WorkflowRefId, WorkflowName, NodeId, NodeName, IsEnabled
    FROM dbo.IntegrationUsages
    WHERE IntegrationId = @IntegrationId
    ORDER BY WorkflowName, NodeName;
END
GO
