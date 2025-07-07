/*
    Procedure: dbo.PublishersChannels_BulkDelete
    Purpose: Soft deletes (sets Deleted=1) all matching publisher-channel records in bulk.
    Parameters:
        - @PublisherChannelData PublisherChannelTableType READONLY: Table of PublisherId/ChannelId pairs
    Returns: None (updates Deleted and LastModified fields)
    Author: Richard Joy
    Date: 2025-04-25
    Last Modified: 2025-04-25 by Richard Joy - Initial version
*/
CREATE PROCEDURE dbo.PublishersChannels_BulkDelete
    @PublisherChannelData PublisherChannelTableType READONLY
AS
BEGIN
    SET NOCOUNT ON;

    -- Soft delete by setting Deleted = 1 for all matching records
    UPDATE pc
    SET pc.Deleted = 1,
        pc.LastModified = CURRENT_TIMESTAMP
    FROM dbo.PublishersChannels pc
    INNER JOIN @PublisherChannelData AS Source
        ON pc.PublisherId = Source.PublisherId
        AND pc.ChannelId = Source.ChannelId;
END; 