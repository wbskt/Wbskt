CREATE PROCEDURE dbo.Client_UpdatePresence
    @Id INT,
    @IsConnected BIT,
    @LastActivityAt DATETIME2(3)
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.Clients
    SET
        IsConnected = @IsConnected,
        LastActivityAt = @LastActivityAt,
        -- ConnectedAt anchors uptime: set on connect, cleared on disconnect
        ConnectedAt = CASE WHEN @IsConnected = 1 THEN @LastActivityAt ELSE NULL END
    WHERE Id = @Id;
END
GO
