CREATE PROCEDURE dbo.ClientsChannels_Remove
    @ClientId INT,
    @ChannelId INT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.ClientsChannels
    SET Deleted = 1, LastModified = CURRENT_TIMESTAMP
    WHERE ClientId = @ClientId AND ChannelId = @ChannelId;
END
