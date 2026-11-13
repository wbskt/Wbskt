CREATE VIEW [admin].MessageUsage
AS
SELECT
    MU.WorkspaceId,
    MU.Day,
    MU.Inbound,
    MU.Outbound
FROM dbo.WorkspaceMessageUsage MU;
GO
