-- The client's id and the workspace that owns it, for an ownership check that needs nothing else.
-- No row when the client does not exist.
CREATE PROCEDURE dbo.Client_FindOwnerBy_RefId
    @RefId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        Id,
        WorkspaceId
    FROM dbo.Clients
    WHERE RefId = @RefId;
END
