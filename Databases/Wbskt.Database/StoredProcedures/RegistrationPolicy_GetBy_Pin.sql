-- The anonymous registration path's lookup: the policy and how many clients it has registered, for an
-- early refusal at its limit. No connected count: nothing on that path reads it.
CREATE PROCEDURE dbo.RegistrationPolicy_GetBy_Pin
    @Pin NVARCHAR(20)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT 
        Id,
        RefId,
        WorkspaceId,
        Pin,
        Name,
        MaxClients,
        AutoApproval,
        IsEnabled,
        CreatedAt,
        (SELECT COUNT(*) FROM dbo.Clients c WHERE c.PolicyId = rp.Id AND c.Status = 1) AS RegisteredClientCount,
        -- Not counted. Kept for one release so a management host on the previous image, whose reader
        -- requires the column, keeps registering while the hosts restart after the migration; dropped
        -- in the release after (#1059).
        0 AS ConnectedClientCount
    FROM dbo.RegistrationPolicies rp
    WHERE Pin = @Pin;
END
GO
