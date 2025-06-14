CREATE PROCEDURE dbo.ClientsChannels_Remove
    @ClientId INT,
    @ChannelId INT
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.ClientsChannels
    WHERE ClientId = @ClientId AND ChannelId = @ChannelId;
END
