-- The credits @WorkspaceId's runs still going may yet spend (dbo.RunCreditsHeld, Reserved). With
-- dbo.WorkspaceCreditsSpent, what Run_Start holds a workspace's credit cap to, so runs going at once can
-- never spend past it between them.
CREATE FUNCTION dbo.WorkspaceCreditsReserved (@WorkspaceId INT)
RETURNS TABLE
AS
RETURN
(
    SELECT ISNULL(SUM(h.Credits), 0) AS Credits
    FROM dbo.RunCreditsHeld(@WorkspaceId, NULL) h
    WHERE h.Kind = N'Reserved'
);
GO
