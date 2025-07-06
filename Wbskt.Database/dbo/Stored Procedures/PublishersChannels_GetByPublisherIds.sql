CREATE PROCEDURE dbo.PublishersChannels_GetByPublisherIds
    @PublisherIds IdListTableType READONLY
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        PublisherId,
        ChannelId,
        LastModified,
        Deleted
    FROM dbo.PublishersChannels
    WHERE PublisherId IN (SELECT Id FROM @PublisherIds)
    AND Deleted = 0
    ORDER BY PublisherId, ChannelId;
END 