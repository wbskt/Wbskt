CREATE PROCEDURE dbo.PublishersChannels_GetByChannelIds
    @ChannelIds IdListTableType READONLY
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        PublisherId,
        ChannelId,
        LastModified,
        Deleted
    FROM dbo.PublishersChannels
    WHERE ChannelId IN (SELECT Id FROM @ChannelIds)
    AND Deleted = 0
    ORDER BY ChannelId, PublisherId;
END 