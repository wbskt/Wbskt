CREATE PROCEDURE dbo.PublishersChannels_Delete
    @PublisherId INT,
    @ChannelId INT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.PublishersChannels 
    SET Deleted = 1, LastModified = CURRENT_TIMESTAMP 
    WHERE PublisherId = @PublisherId AND ChannelId = @ChannelId;
END 