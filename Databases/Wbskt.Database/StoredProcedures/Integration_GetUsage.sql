-- The steps that use an integration (see dbo.IntegrationUsages for which version of each workflow).
CREATE PROCEDURE dbo.Integration_GetUsage
    @IntegrationId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT WorkflowRefId, WorkflowName, NodeId, NodeName, IsLive
    FROM dbo.IntegrationUsages
    WHERE IntegrationId = @IntegrationId
    ORDER BY WorkflowName, NodeName;
END
GO
