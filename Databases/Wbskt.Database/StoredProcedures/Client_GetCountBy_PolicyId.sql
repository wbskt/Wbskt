-- No caller from this release: the policy lookups return RegisteredClientCount. Kept for one release so a
-- management host on the previous image keeps registering while the hosts restart after the migration;
-- dropped, with a pre-deployment DROP, in the release after (#1059).
CREATE PROCEDURE dbo.Client_GetCountBy_PolicyId
    @PolicyId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT COUNT(Id)
    FROM dbo.Clients
    WHERE PolicyId = @PolicyId
      AND Status = 1; -- 1: Registered
END
GO
