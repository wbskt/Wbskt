-- How many clients a workspace (or one of its policies) has in each status, and how many are
-- connected, in one read: the console's status bar and a policy page's filter chips.
CREATE PROCEDURE dbo.Client_GetCounts
    @WorkspaceId INT,
    @PolicyId INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        COUNT(*) AS Total,
        COUNT(CASE WHEN Status = 0 THEN 1 END) AS Pending,
        COUNT(CASE WHEN Status = 1 THEN 1 END) AS Registered,
        COUNT(CASE WHEN Status = 2 THEN 1 END) AS Revoked,
        COUNT(CASE WHEN Status = 3 THEN 1 END) AS Rejected,
        COUNT(CASE WHEN IsConnected = 1 THEN 1 END) AS Connected
    FROM dbo.Clients
    WHERE WorkspaceId = @WorkspaceId
      AND (@PolicyId IS NULL OR PolicyId = @PolicyId);
END
GO
