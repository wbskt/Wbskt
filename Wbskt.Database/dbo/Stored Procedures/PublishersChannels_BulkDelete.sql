CREATE PROCEDURE dbo.PublishersChannels_BulkDelete
    @PublisherChannelData PublisherChannelTableType READONLY
AS
BEGIN
    SET NOCOUNT ON;

    -- Soft delete by setting Deleted = 1 for all matching records
    UPDATE dbo.PublishersChannels 
    SET Deleted = 1, LastModified = CURRENT_TIMESTAMP 
    WHERE EXISTS (
        SELECT 1 FROM @PublisherChannelData AS Source 
        WHERE PublishersChannels.PublisherId = Source.PublisherId 
        AND PublishersChannels.ChannelId = Source.ChannelId
    );
END 