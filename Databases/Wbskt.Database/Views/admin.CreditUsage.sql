CREATE VIEW [admin].CreditUsage
AS
SELECT
    CU.WorkspaceId,
    CU.Day,
    CU.Credits
FROM dbo.WorkspaceCreditUsage CU;
GO
