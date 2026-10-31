-- Where a command for this client goes: its workspace (the sender's must match) and the socket host
-- holding its connection, and its status (only a registered device may have commands wait for it).
-- No row when the client does not exist.
CREATE PROCEDURE dbo.Client_GetCommandTargetBy_RefId
    @RefId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        Id,
        WorkspaceId,
        IsConnected,
        ConnectedHostId,
        Status
    FROM dbo.Clients
    WHERE RefId = @RefId;
END
GO
