-- Where a command for this client goes: its workspace (the sender's must match) and the socket host
-- holding its connection. No row when the client does not exist.
CREATE PROCEDURE dbo.Client_GetCommandTargetBy_RefId
    @RefId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        Id,
        WorkspaceId,
        IsConnected,
        ConnectedHostId
    FROM dbo.Clients
    WHERE RefId = @RefId;
END
GO
