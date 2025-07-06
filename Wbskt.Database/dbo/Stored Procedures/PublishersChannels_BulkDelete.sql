CREATE PROCEDURE dbo.PublishersChannels_BulkDelete
    @PublisherChannelData PublisherChannelTableType READONLY
AS
BEGIN
    SET NOCOUNT ON;

    -- Soft delete by setting Deleted = 1 for all matching records
    UPDATE pc
    SET pc.Deleted = 1, pc.LastModified = CURRENT_TIMESTAMP 
    FROM dbo.PublishersChannels pc
    INNER JOIN @PublisherChannelData AS Source 
        ON pc.PublisherId = Source.PublisherId 
        AND pc.ChannelId = Source.ChannelId;
END 